# 검토: TCP 아웃바운드 프레이밍 + 현재 설계·구현 상태 (HEAD f316d11)

- 날짜: 2026-06-18
- 검토자: Claude (계획·검토 담당)
- 검증 HEAD: `f316d11` (feat: frame tcp outbound fan-out)
- 직전 검토: `.claude/review/2026-06-17-impl-vs-design-cross-verification.md` (HEAD `aee637e`, G1 미해소 상태)
- 설계 근거: spec `docs/superpowers/specs/2026-06-18-tcp-outbound-framing-policy-design.md`, 결정 D065
- 상태: **설계와 정합. 직전 검토의 유일한 실질 갭(G1, TCP 아웃바운드 무프레이밍)이 해소됨. 신규 결함 없음.**

## 1. 회귀 게이트 (직접 실행)

- `dotnet build HighPerformanceSocket.slnx` → 경고 0 / 오류 0.
- `dotnet test HighPerformanceSocket.slnx` → 실패 0, **통과 135**
  (Protocol 33 / Transport 43 / Buffers 18 / Server 11 / Broker 30). 직전 134 + 프레이밍 테스트 1.

## 2. 한 줄 판정

직전 교차검증이 1순위로 지목한 **G1(broker→TCP subscriber 아웃바운드에 메시지 경계 없음)**이
`f316d11`로 해소됐다. 인바운드와 동일한 `4B big-endian length prefix + payload` 프레임을 아웃바운드에도
적용하되, **fan-out zero-copy(D009/D057) 불변식을 깨지 않고** 구현했다.

## 3. G1 해소 — 구현 검증

### 설계 핵심 불변식 충족 여부

| 불변식 | 충족 | 근거 |
|---|---|---|
| 구독자당 payload 복사 0회 | ✅ | `TransportSendBuffer.WithLengthPrefix()`는 값 타입 metadata(`_prependLengthPrefix` flag)만 바꾼다. payload는 기존 `RefCountedBuffer + offset + length` slice 공유. |
| header/payload = 1 논리 송신 항목 | ✅ | `SendInFlightAsync`가 한 in-flight handle 수명 안에서 header→payload 순차 전송. SAEA 송신은 연결당 단일 pump라 항목 간 interleaving 없음. |
| header만 남거나 payload만 drop 되는 상태 없음 | ✅ | drop-oldest/close는 `TransportSendBuffer`(payload ref) 단위로만 동작. header는 큐에 없고 pump가 즉석 생성. |
| payload ref 정확히 1회 Release | ✅ | header는 자체 ref 없음. payload ref는 `InFlightSend.ReleaseOnce()`(idempotent)로 Complete/Dispose 어느 경로든 1회. "header 송신 성공→payload SocketException" 경로도 `using(inFlight)` Dispose가 1회 Release, 누수·이중해제 없음. |
| UDP는 length prefix 미적용 | ✅ | `BrokerSubscriber.TrySend`가 TCP 분기에서만 `WithLengthPrefix()`. UDP는 raw datagram(`1 datagram=1 message`). |

### 추가 비용

- header 4B는 `_sendHeaderPool`(BlockSize=4 pinned pool)에서 **send pump당 1회** 대여(`SendLoopAsync` 진입 시 1회, finally 반환).
  구독자 수·메시지 수와 무관하게 연결당 buffer 1개. zero-copy 원칙과 부합.

### 테스트 품질

- `TcpCommandLoopback_When...VariableLengthMessages_...LengthPrefixedFrames`: `firstPayload`의 앞 4바이트를
  길이 3처럼 구성(`{0,0,0,3, 170,187,204}`) → 무프레이밍 구현이면 수신측 frame reader가 payload 일부만 읽고
  두 번째 메시지 정렬이 깨지도록 고정한 **유효한 Red 설계**. 가변 길이 2메시지 연속 fan-out + `RentedCount==0` 검증.
- 샘플 subscriber / benchmark receive path도 length-prefixed 수신으로 갱신됨(완료 기준 "오래된 raw outbound 설명 제거" 충족).

## 4. 미커밋 문서 변경 (working tree)

`AGENTS.md` / `PLAN.md` / `2026-06-16-interface-server-endpoint-model-design.md` 3건은 **용어 정렬·설명 보강**이며
신규 프레이밍 결정과 충돌 없음:
- "pub/sub 메시지 브로커" → "Interface Server(내부 topic 기반 pub/sub broker 메커니즘, D053)" 일관 정렬.
- endpoint-model spec: high-watermark가 endpoint identity가 아닌 transport-kind별 lifetime max임을 명확화,
  capacity 16에서 HWM 포화 → drop count와 함께 해석하라는 주석 추가. 이미 구현된 동작의 문서화로 정합.
- 권고: 이 3건은 별도 `docs:` 커밋으로 정리(코드 변경과 섞지 말 것).

## 5. 유지되는 미결 항목 (갭 아님, 우선순위 순)

- **P-Backpressure**: load runner가 closed-loop라 D012 drop-oldest 경로가 실측에서 한 번도 실행되지 않음.
  open-loop(소비자 무관 100Hz 발사) 시나리오로 큐 적체·drop을 실제 stress하는 후속 필요
  (`overall-state-2026-06-15.md` §8과 동일). 기능 위험은 없으나 "지속 부하 큐 안정" 주장은 여전히 미stress.
- **O1 EndpointId 충돌**: transport 인스턴스 2개 이상 시 standalone static id와 transport별 id가 겹칠 수 있음.
  D058로 진단 전용·snapshot은 transport별이라 현재 영향 0. 후속 메모 유지.
- **O2 EndpointState 2값만 산출**: enum 4값 중 Open/Closed만. forward-looking 계약으로 정상.
- **L1 D008 topic entry 영구 누적**: distinct-topic 수 비례, churn 무관이라 우선순위 낮음.
- **범위 밖(설계 확정)**: TCP/subscribe ack, protocol error response, reliable/durable delivery, reconnect
  subscription transfer, UDP 신뢰성/length prefix, RIO/io_uring(Phase 5/6), latency hard SLO gate.

## 6. 결론

직전 검토의 1순위 갭(G1)이 zero-copy 불변식을 지키며 해소됐고, 빌드/테스트·소유권·종료 규율 모두 양호.
**v1 범위의 동작하는 크로스플랫폼 Interface Server(TCP+UDP pub/sub, 아웃바운드 프레이밍 포함)가 완성**됐다.
다음 게이트는 새 기능보다 **(a) 미커밋 문서 정리 커밋**, **(b) open-loop 백프레셔 stress 시나리오**로
D012 경로를 실측 검증하는 것을 권한다. 그 다음이 Phase 5/6 커널 백엔드다.

## 7. 다시 검증할 때 체크리스트

- [ ] open-loop 부하에서 큐 깊이·drop count가 D012대로 거동하고 종료 후 `RentedCount==0` 유지하는가.
- [ ] O1: 다중 transport snapshot 병합 시 EndpointId 충돌 대응 결정.
- [ ] O2: EndpointState 4값 산출 여부 결정/문서화.
- [ ] 전체 테스트 실패 0 유지.
