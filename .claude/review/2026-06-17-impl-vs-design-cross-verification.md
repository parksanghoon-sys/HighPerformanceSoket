# 교차검증: 현재 구현 상황 ↔ Interface Server 설계 (HEAD aee637e)

- 날짜: 2026-06-17
- 검토자: Claude (계획·검토 담당)
- 검증 HEAD: `aee637e` (docs: align backpressure default policy)
- 직전 검토: `.claude/review/2026-06-16-endpoint-model-cross-verification.md` (HEAD `a5b08d5` 기준, **현재 stale**)
- 설계 근거:
  - spec `docs/superpowers/specs/2026-06-16-interface-server-endpoint-model-design.md`
  - spec `docs/superpowers/specs/2026-06-16-udp-broker-runtime-target-wire-control-design.md`
  - 결정 D053, D055, D058, D059, D060, D062, D063, D064
- 상태: **설계와 정합. 코드 결함(소유권/누수/경합/종료) 없음.** 단, 그린 테스트에 가려진 **실질 갭 1건**(TCP 아웃바운드 무프레이밍) 존재.

---

## 0. 검토 경위 메모 (중요)

직전 교차검증(`a5b08d5`)과 이번 검토 사이에 저장소가 7커밋 전진했다(`bbc543e` → `aee637e`).
세션 초기 스냅샷이 `bbc543e`였던 탓에 1차 판단에서 "UDP end-to-end 미결선"이라 적었으나,
**현재 HEAD 기준 그 판단은 무효다.** 아래는 실제 현재 트리(`aee637e`)를 직접 읽고 빌드/테스트한 결과다.

## 1. 회귀 게이트 (직접 실행)

- `dotnet build HighPerformanceSocket.slnx`: 경고 0 / 오류 0.
- `dotnet test HighPerformanceSocket.slnx`: 실패 0, **통과 134**
  (Protocol 33 / Transport 43 / Buffers 18 / Server 10 / Broker 30). 상태 문서의 134 주장과 일치.
- `git diff --check`: whitespace 오류 없음 (CRLF 변환 경고만).

## 2. 단계적 전환 — v1 범위 내 전부 완료

| 단계 | 설계 의도 | 구현 | 상태 |
|---|---|---|---|
| 1 | send queue high-watermark 먼저 | `TransportBase` transport-wide CAS max + per-endpoint max, enqueue 직후 갱신 | ✅ |
| 2 | EndpointId + endpoint snapshot | `EndpointId/Kind/State`, `EndpointSnapshot`, `ITransportEndpointDiagnostics`, 값 전용 | ✅ |
| 3 | SubscriptionTable value 를 endpoint-target 으로 | `BrokerSubscriber` 값 키 (EndpointId는 D058로 진단 전용) | ✅ |
| 4 | UDP endpoint subscription 결선 | `BrokerUdpDatagramHandler` + `BrokerServer.StartUdpAsync` + UDP loopback 통합 테스트 | ✅ |

UDP 경로는 **D060 정책 그대로**: datagram self-command(`SUBSCRIBE`/`UNSUBSCRIBE`/`PUBLISH`),
runtime target = `(IUdpEndpoint, remote EndPoint)`, malformed datagram 은 shared endpoint 를 닫지 않고 폐기,
endpoint close 시 `SubscriptionTable.UnsubscribeAll(IUdpEndpoint)` 로 정리.

## 3. 불변식 (코드에서 확인, 결함 없음)

- **D009 무복사 팬아웃**: `BrokerPublisher.TrySendToSubscriber` 가 구독자당 `AddRef`→`TrySend`/`TrySendTo`,
  실패 시 즉시 `Release`. `BrokerUdpDatagramHandler` PUBLISH 는 수신 datagram 을 guard ref 로 들고
  `finally` 에서 1회 Release. 혼합 TCP+UDP fan-out 이 같은 ref 를 양쪽 전송에 넘기는 것을 테스트가 확인.
- **D012 drop-oldest**: `TransportConnection.TryAcceptSend` / `SaeaUdpEndpoint.TryAcceptSend` 가 evict 를
  락 안에서 끝내고 Release 는 락 밖, 정확히 1회. close 는 남은 pending 만 drain.
- **D011 종료**: `Close()` 가 락 안에서 pending drain+Release 후 enqueue reject.
- **고정 풀 I/O**: TCP recv = pinned `_receivePool`, UDP = `RentCounted()` 직접 recv.
- **백엔드 은닉**: Broker/Protocol 이 backend 를 모름.

## 4. 핵심 발견 — TCP 아웃바운드 무프레이밍 (실질 갭)

### G1 — broker→subscriber 전송에 길이 프리픽스/구분자가 없다

- **인바운드** client→broker: 4B big-endian 길이 프리픽스 프레임(`TcpFrameAssembler`).
- **아웃바운드** broker→subscriber: `BrokerPublisher.Publish` 가 `TransportSendBuffer(payload, offset, length)` —
  **payload 슬라이스 원본만** 전송. 프레임 헤더를 붙이지 않는다.
- **결과**: TCP 구독자가 2개 이상 메시지를 받으면 경계 없는 연속 바이트 스트림이 되어 메시지 구분이 불가능하다.
  (UDP 는 `1 datagram = 1 message` 라 무관. TCP 구독자 한정.)

### 왜 134개 테스트가 못 잡나

- `tests/Hps.Benchmarks/TcpLoopbackScenarioRunner.cs:267` — `ReceiveExactAsync(subscriber, BenchmarkTargets.PayloadBytes)`,
  즉 **항상 정확히 4096B** 를 읽는다. 와이어의 길이 정보가 아니라 수신측이 크기를 미리 아는 out-of-band 가정이다.
- 모든 메시지가 동일 4096B 라서 통과하며, 가변 길이였다면 깨진다.
- closed-loop `--load` 는 한 번에 1개만 in-flight 라 경계 문제가 드러나지 않는다.

### 판정

- **문서화된 의도적 보류**다 — DECISIONS.md:283-284
  "subscriber 출력은 아직 message framing 이 아니라 TCP receive chunk 단위이며, 서버 outbound framing/ack 정책이 생기면
  샘플도 그 계약에 맞춰 갱신한다." 따라서 숨은 버그는 아니다.
- 그러나 "외부 정보를 구독 endpoint 로 발행하는 Interface Server" 헤드라인 목표가 **일반적(가변 길이·다중 메시지)
  TCP 전달에 대해선 미완**이며, 이 미완이 그린 테스트에 가려져 있다는 점을 분명히 한다.
- `TODOS.md`/`CURRENT_PLAN.md` 의 "범위 밖" 목록에 outbound framing 항목이 없다(`protocol error 응답`만 존재).
  명시 권장.

## 5. 부수 관찰 (영향 미미)

- **O1 — EndpointId 충돌 가능성**: `TransportConnection.cs` 의 `s_nextStandaloneEndpointId`(static, 프로세스 전역)와
  `TransportBase._nextEndpointId`(transport 인스턴스별)가 분리돼, transport 2개 이상이면 EndpointId 가 겹칠 수 있다.
  D058 로 진단 전용이고 snapshot 은 transport 별이라 현재 영향 0. 여러 transport snapshot 을 합치면 충돌하므로 후속 메모.
- **O2 — EndpointState 2값만 산출**: enum 은 Open/Closing/Closed/Faulted 4값이나 `CreateSnapshot` 은 Open/Closed 만 낸다.
  forward-looking 계약으로 정상. 4값 산출 여부는 후속 판단(직전 검토 F2 유지).
- **O3 — last drop reason/timestamp 미구현**: D062 로 전용 필드 미추가 결정. kind/endpoint 누적 drop 으로 대체.

## 6. 설계가 명시적으로 v1 밖으로 둔 항목 (갭 아님)

- reconnect rebinding / stable subscriber identity: D059.
- UDP stale remote idle expiry, drop log/sampling, Server convenience diagnostics: P2_LATER.
- latency hard SLO gate: D063 (관측값 유지, hard gate 는 누수0/drop0/payload-errors0).
- RIO/io_uring 백엔드: Phase 5/6.

## 7. 권고

1. **G1 을 다음 단위 1순위 후보로**: TCP 아웃바운드 message framing/ack 정책을 결정·구현하고,
   가변 길이 다중 메시지 fan-out 을 와이어 프레이밍으로 구분하는 통합 테스트를 추가한다.
   (현재 벤치마크의 고정 4096B 가정을 와이어 길이 기반으로 교체)
2. **범위 명시**: 결정 전까지 `TODOS.md`/`CURRENT_PLAN.md` "범위 밖" 에 outbound framing 을 명시해
   "구현됐다"는 오해를 막는다.
3. O1 은 한 줄 주석 또는 후속 메모로 남긴다.

## 8. 다시 검증할 때 체크리스트

- [ ] G1: 아웃바운드 framing 정책이 결정·구현되고 가변 길이 다중 메시지 fan-out 이 와이어로 구분되는가.
- [ ] 벤치마크 수신이 고정 크기 가정 대신 와이어 길이로 메시지를 자르는가.
- [ ] O2: EndpointState 4값 산출 여부 결정/문서화.
- [ ] 전체 테스트 실패 0 유지.
