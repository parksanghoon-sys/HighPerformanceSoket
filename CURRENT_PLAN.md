# CURRENT_PLAN.md — 현재 실행 지점

## 최종 목표
고성능 소켓 기반 pub/sub 메시지 브로커를 구현한다. 우선 사용 목표는 **4096 bytes 메시지를 100 Hz로 지연 누적 없이 처리**하는 것이다.

현재 해석:
- 단일 메시지 크기: 4096 bytes.
- 단일 스트림 기준 빈도: 100 Hz, 즉 약 409.6 KB/s payload.
- “딜레이 없이”는 현재 정량 latency SLO가 아니므로, 우선은 지속 부하에서 큐 적체가 누적되지 않고 p99 지연이 안정적으로 유지되는 상태로 해석한다.
- Phase 4 벤치마크 단계에서 p50/p99 지연, 처리량, 큐 길이, 누수 여부를 측정 가능한 기준으로 확정한다.

## 현재 Phase
Phase 1 — 메모리 계층 `src/Hps.Buffers/`.

## 확인된 현재 상태
- Phase 0 스캐폴딩은 존재한다.
  - `HighPerformanceSocket.slnx`
  - `Directory.Build.props`
  - `src/Hps.Buffers`
  - `tests/Hps.Buffers.Tests`
- `src/Hps.Buffers/BipBuffer.cs`의 검토 must-fix M1·M2는 이번 사이클에서 반영됐다.
- **구현 전 설계 결정은 모두 종결됨**(DECISIONS D005~D012). `.claude/review/`에 검토 6건:
  - `phase1-bipbuffer.md` — BipBuffer must-fix **2건(M1, M2)**. (D005)
  - `phase1-refcounted-pool.md` — RefCountedBuffer/Pool 설계 **승인**(AddRef 순서 계약). (D006)
  - `phase2-transport-bipbuffer.md` — 송신 다중생산자(D1)·버퍼 소유권(D2)·백프레셔(D3). (D007)
  - `phase3-broker-routing.md` — 빈 토픽 eager-cleanup 경합(R1) 금지. (D008)
  - `phase3-publish-ownership.md` — recv→팬아웃 핸드오프(TCP 1회 복사 / UDP 직접 recv). (D009)
  - `phase3-framing-and-close.md` — TCP 프레임 조립(D010)·종료 release 계약(D011)·drop-oldest evict release(D012).
- `tests/Hps.Buffers.Tests/BipBufferTests.cs`가 추가됐고 M1/M2 회귀 테스트, deterministic edge 테스트,
  seeded fuzz 테스트가 discover된다.
- `BipBuffer` 내부의 `Volatile.Read/Write` 호출은 committed count, producer/consumer cursor, watermark snapshot/publish helper로 정리됐다.
- `BipBuffer`와 `RefCountedBuffer`의 private helper에는 snapshot/publish 의미와 수명/소유권 경계 주석이 보강됐다.
- `src/Hps.Buffers/PinnedBlockMemoryPool.cs`가 추가됐고 최소 API 테스트가 discover된다.
- `PinnedBlockMemoryPool` 멀티스레드 대여/반환 스트레스 테스트가 추가됐다.
- `PinnedBlockMemoryPoolTests`는 reflection 기반 `PoolApi` 래퍼 없이 public API를 직접 호출하도록 정리됐다.
- `src/Hps.Buffers/RefCountedBuffer.cs`가 추가됐고 최소 참조계수/반환 계약 테스트가 discover된다.
- `PinnedBlockMemoryPool.RentCounted()`가 추가되어 counted buffer 가 마지막 `Release()`에서 풀로 돌아간다.
- `RefCountedBuffer` 내부의 `Volatile.Read/Write` 호출은 의도 기반 helper로 감싸져 수명/길이 상태 관측 의미가 드러나도록 정리됐다.
- `RefCountedBuffer` 동시 Release/팬아웃 스트레스 테스트가 추가되어 구독자 수 가변 fan-out과 다수 buffer in-flight 반환을 검증한다.
- 재확인: `dotnet test HighPerformanceSocket.slnx`는 테스트 18개를 실행했고 모두 통과했다.
- 재확인: `dotnet build HighPerformanceSocket.slnx`는 경고 0개, 오류 0개로 통과했다.
- D013 기준으로 이번 기능 단위 완료 후 다음 구현은 사용자 리뷰 뒤 진행한다.

## 다음 단일 작업 단위
사용자 리뷰 대기.

리뷰 후 계속 진행 지시가 있으면 다음 단일 작업 단위는 Phase 2 착수 전에 `ITransport`와 버퍼 소유권 계약을 구체화하는 것이다.
이 작업은 Transport/Protocol/Broker 전체 구현이 아니라, 풀 핸들(`RefCountedBuffer`/lease) 기반 인터페이스와 반환 책임을
작게 정의하는 단위로 제한한다.

## 이번 단위의 검증 경로
- `dotnet test tests\Hps.Buffers.Tests\Hps.Buffers.Tests.csproj --filter "FullyQualifiedName~RefCountedBufferTests"`
- `dotnet test HighPerformanceSocket.slnx`
- `dotnet build HighPerformanceSocket.slnx`
- 테스트 출력에서 `Hps.Buffers.Tests`의 실제 테스트 18개가 discover되고 실행됐는지 확인한다.
- 결과: focused 통과 7, 실패 0, 건너뜀 0. 전체 통과 18, 실패 0, 건너뜀 0. 빌드 경고 0, 오류 0.

## 이번 작업에서 건드리지 않은 범위
- `Hps.Transport`
- Protocol/Broker/Server
- RIO/io_uring 백엔드

위 범위는 사용자 리뷰 후 다음 단일 작업 단위에서 필요 범위만 다시 확인하고 진행한다.
