# AGENTS.md — 고성능 소켓 Interface Server 핵심 규칙

이 파일은 구현 에이전트(Codex)가 **항상 지켜야 하는 핵심 규칙·불변식**이다.
"무엇을 만드는가"의 단계별 작업은 [`PLAN.md`](./PLAN.md) 를 따른다.
설계/코드 검토 결과는 `.claude/review/` 에 누적된다(검토는 Claude가 담당).

---

## 0. 한 줄 요약
Span 기반으로 **메모리 복사를 최소화**한 고성능 TCP/UDP **Interface Server**
(내부적으로 topic 기반 **pub/sub broker 메커니즘**으로 동작, D053).
관리힙 복사뿐 아니라 가능한 경우 **커널 단 복사까지** 줄인다(Windows=RIO, Linux=io_uring).

---

## 1. 빌드 / 런타임 제약 (변경 금지)
- TFM: `net9.0`. LangVersion: **`8.0`** (C# 8.0 문법만 사용).
- `Nullable=enable`, `ImplicitUsings=disable`, `AllowUnsafeBlocks=true`.
- 위 설정은 루트 `Directory.Build.props` 에서 일괄 적용한다. 개별 csproj 에서 재정의하지 말 것.
- LangVersion 8.0 이므로 **금지**: global using, file-scoped namespace, record, target-typed `new()`,
  switch expression 의 C#9+ 패턴, `init` 접근자. → 고전적 C# 8 스타일로 작성한다.
- 모든 `using` 은 파일 상단에 명시한다.

## 2. 아키텍처 불변식 (절대 규칙)
1. **구독자당 복사·불필요한 관리힙 복사 금지.** 전 구간 `Span<byte>`/`Memory<byte>`/`ReadOnlySpan<byte>`.
   예외(허용): TCP publish 에서 recv 링의 payload 를 공유 메시지 버퍼(`RefCountedBuffer`)로 옮기는
   **1회 복사**는 허용한다(소유권/수명 때문에 불가피, D009). UDP publish 는 0회(직접 recv). 이 1회를 빼면
   어디서도 중복 복사를 만들지 말 것.
2. **모든 I/O 버퍼는 고정(pinned) 풀에서 대여**한다. `PinnedBlockMemoryPool` 외 경로로 I/O 버퍼를
   `new byte[]` 할당하지 말 것. (RIO `RegisterBuffer` / io_uring fixed buffer 등록 + GC 이동 방지 목적)
3. **send/recv 원형 큐는 `BipBuffer`** 를 쓴다. 항상 단일 연속 Span 을 보장해 소켓 호출 1회/복사 0회.
   일반 2-segment 링버퍼로 대체하지 말 것.
   단, 현재 크로스플랫폼 `SaeaTransport` 기준선은 D023/D024/D045에 따라 raw Socket 계약 검증용으로
   pinned receive block 과 `TransportSendBuffer` direct send 를 허용한다. 이 예외는 SAEA 기준선에만 적용되며,
   RIO/io_uring 또는 명시적 송수신 큐 최적화 단계에서는 `BipBuffer` 원칙을 다시 적용한다.
4. **`BipBuffer` 는 SPSC** (단일 생산자=소켓 recv, 단일 소비자=파서) 로만 사용한다.
   생산자는 `_write`/`_watermark`, 소비자는 `_read` 만 전진. 락 추가 금지(가시성은 Volatile/Interlocked).
5. **팬아웃(fan-out)은 구독자당 복사 0회.** 발행 페이로드는 `RefCountedBuffer` 1개로 보관하고, 각 구독자
   송신 큐에는 (버퍼참조 + offset + length) 만 enqueue 한다. 구독자 수만큼 페이로드를 복사하지 말 것.
   소유권/수명 규칙은 D009 및 `.claude/review/phase3-publish-ownership.md` 를 따른다(publish 가드 ref,
   enqueue 실패 시 즉시 Release). 송신 경로는 **MPSC 큐 → 단일 펌프 → SPSC 송신 BipBuffer**(D007).
   SAEA 기준선은 현재 pending queue → 단일 raw Socket 펌프까지만 구현하며, SPSC 송신 BipBuffer 적용은
   D045에 따라 최적화 backend 또는 후속 송수신 큐 단위에서 다룬다.
   백프레셔 drop-oldest 는 evict 한 `RefCountedBuffer` 를 정확히 1회 Release, evict/dequeue/close 를
   단일 락으로 직렬화한다(D012).
6. **OS별 백엔드는 `ITransport` 뒤에 숨긴다.** 상위 계층(Protocol/Broker)은 백엔드를 몰라야 한다.
   런타임에 OS + capability 프로브로 백엔드를 고르고, 불가 시 `SaeaTransport` 로 폴백한다.
7. **프레이밍**: TCP = `4바이트 big-endian 길이 프리픽스 + 페이로드`. UDP = `1 datagram = 1 메시지`.
   TCP 파서는 **copy 기반 per-connection 조립 상태머신**(D010): 헤더 4B 누적(분할 처리) → payload를
   `RefCountedBuffer`로 누적 복사. recv 링이 프레임을 통째로 담을 필요 없음(payload > recv 링 허용).
   `maxPayload` 상한으로 과대 할당/DoS 방지.
8. **연결 종료 계약(D011)**: `IConnection.Close()/Dispose()` 는 송신 큐 pending·in-flight·조립 중
   `RefCountedBuffer` 를 모두 `Release` 하고 이후 enqueue 를 원자적으로 reject 한다. 종료 후 `RentedCount==0`.

## 3. 프로젝트 레이아웃 (계층 경계)
```
src/Hps.Buffers/           메모리 계층: PinnedBlockMemoryPool, BipBuffer, RefCountedBuffer
src/Hps.Transport/         ITransport/IConnection 추상화 + SaeaTransport(크로스플랫폼 기준선)
  Abstractions/            public Transport 계약: ITransport/IConnection/버퍼 view/handler/endpoint
  Runtime/                 공통 런타임: TransportBase, TransportConnection, TransportFactory
  Saea/                    크로스플랫폼 SAEA/raw Socket 기준선 구현
src/Hps.Transport.Rio/     Windows RIO 백엔드 (P/Invoke)
src/Hps.Transport.IoUring/ Linux io_uring 백엔드 (P/Invoke)
src/Hps.Protocol/          프레이밍/코덱
src/Hps.Broker/            내부 pub/sub 라우팅 + 팬아웃 메커니즘
src/Hps.Server/            Interface Server 실행 호스트(현재 TCP broker host)
samples/                   Hps.Sample.Publisher / Hps.Sample.Subscriber
tests/                     각 계층 단위/통합 테스트 + Hps.Benchmarks
```
- 의존 방향은 위 표의 아래→위로만(상위가 하위를 참조). 역참조·순환참조 금지.
- 하위 계층은 상위를 모른다. `Hps.Buffers` 는 어떤 소켓/프로토콜도 참조하지 않는다.

## 4. 코딩 규칙
- **모든 문서·주석·설명은 한국어로 작성한다.** (코드 식별자만 영어, 그 외 산출물 전부 한글)
- **주석을 상세히 단다.** 무엇을 하는지뿐 아니라 **왜** 그렇게 했는지, 동시성 가정·메모리 소유권·
  경계 조건·불변식을 함께 적는다. 기존 파일(`src/Hps.Buffers/BipBuffer.cs`) 의 주석 밀도를 기준으로
  맞추거나 그 이상으로 한다. 비자명한 분기·매직넘버·랩어라운드 로직에는 반드시 설명을 붙인다.
- public API 에는 XML doc 주석(`///`)으로 의도·동시성 가정·소유권을 명시한다.
- 핫패스에서 할당(allocation) 금지: LINQ, 클로저 캡처, 박싱, `params`, `async` 상태머신 남발 주의.
  비동기는 `ValueTask`/`ValueTask<T>` 우선.
- 예외는 프로그래밍 오류(계약 위반)에만. 정상적 흐름 제어에 예외를 쓰지 말 것.
- 새 외부 NuGet 의존성 추가 전 PLAN/검토에서 합의한다(테스트용 xUnit/BenchmarkDotNet 제외).

## 5. 테스트 규칙 (3색 TDD 필수)
- **모든 구현은 3색(Red→Green→Refactor) TDD 사이클을 반드시 지킨다.** 예외 없음.
  1. **Red**: 먼저 실패하는 테스트를 작성하고, 실패(컴파일 실패 아님, 단언 실패)를 **확인**한다.
  2. **Green**: 테스트를 통과시키는 **최소한의** 구현만 추가한다. 과도한 선구현 금지.
  3. **Refactor**: 테스트가 green 인 상태를 유지하며 중복 제거·명확화한다. 매 리팩터 후 재실행.
  → 프로덕션 코드는 항상 "그것을 요구하는 실패 테스트"가 선행해야 한다. 테스트 없이 기능 추가 금지.
- 자료구조(BipBuffer/Pool/RefCount)는 경계·랩어라운드·누수·동시성(SPSC) 케이스와
  무작위(fuzz) 테스트를 포함한다.
- 커밋/Phase 완료 기준: `dotnet build` 무경고에 가깝게 + `dotnet test` 전부 green.
- 백엔드(RIO/io_uring)는 Phase 2/3 의 통합 테스트를 **그대로 재사용**해 회귀를 잡는다.

## 6. 작업 흐름 (Codex ↔ Claude)
1. Codex 는 `PLAN.md` 의 **현재 Phase** 를 구현한다. Phase 순서를 건너뛰지 않는다.
2. Phase 완료 시 빌드/테스트 결과를 남기고 검토를 요청한다.
3. Claude 가 `.claude/review/` 에 검토서(`phaseN-<주제>.md`)를 작성한다.
   검토에서 **must-fix** 로 표시된 항목은 다음 작업 전에 해소한다.
4. 본 AGENTS.md 의 규칙과 충돌하는 지시가 있으면 멈추고 사람에게 확인한다.

## 7. 범위 밖 (지금 만들지 말 것)
- UDP 신뢰성/순서보장/혼잡제어. (커널 zero-copy UDP send 까지만)
- TLS/암호화, 인증, 클러스터링, 영속화(persistence).
- 멀티 컨슈머 `BipBuffer`. 필요해지면 별도 설계로 승격한다.
