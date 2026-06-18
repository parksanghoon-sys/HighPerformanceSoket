# review-status-2026-06-18 — Claude 검토 snapshot 현재화

작성 기준: HEAD `980721c` (`docs: decide server diagnostics surface`), 2026-06-18.

이 문서는 기존 `.claude/review/*.md` 원문을 수정하거나 삭제하지 않고, 작성 당시 snapshot 이 현재 구현과 어떻게 달라졌는지
정리하는 overlay 이다. 개별 검토서는 역사적 근거로 보존하고, 현재 실행 판단은 이 문서와 root state docs
(`CURRENT_PLAN.md`, `TODOS.md`, `DECISIONS.md`, `CHANGELOG_AGENT.md`)를 함께 본다.

## 1. 현재 검증 기준

- `dotnet build HighPerformanceSocket.slnx --no-restore` 통과, 경고 0 / 오류 0.
- `dotnet test HighPerformanceSocket.slnx --no-build --no-restore` 통과.
  - Protocol 33
  - Broker 30
  - Server 12
  - Transport 43
  - Buffers 18
  - 전체 136, 실패 0
- 최근 benchmark baseline:
  - `docs/benchmarks/baselines/2026-06-18/local-latency-baseline.md`
  - `--load` 3회: sent/received 3000, dropped 0, pool-rented 0, p99 879.7~924.1us.
  - `--load-open-loop` 3회: sent/received 3000, dropped 0, pool-rented 0, p99 915.9~1005.5us.

## 2. 현재 한 줄 판정

현재 HEAD 기준 **must-fix 또는 blocker 로 남은 리뷰 항목은 없다.**

v1 범위의 크로스플랫폼 SAEA 기준선은 TCP/UDP pub/sub, TCP outbound length-prefix framing, drop-oldest backpressure,
transport/endpoint diagnostics, benchmark report/baseline 까지 연결됐다. 남은 항목은 구현 결함이 아니라 후속 설계 또는 운영 표면 확장이다.

## 3. 주요 과거 리뷰 항목 현재 상태

| 검토 파일 | 당시 핵심 항목 | 현재 상태 | 현재 근거 |
|---|---|---|---|
| `REVIEW_2026-06-11.md` | Phase 0~1 이후 실제 소켓 I/O/Protocol/Broker 미착수 | Superseded | SAEA TCP/UDP, Protocol, Broker, Server, samples, benchmark 가 모두 존재한다. |
| `overall-state-2026-06-11.md` | H1 백프레셔, H2 연결종료 구독정리, H3 end-to-end 결선 | 해소 | D012/D039/D040/D043/D048, Server TCP/UDP loopback tests, stalled subscriber stress. |
| `overall-state-2026-06-15.md` | P1 Phase 4 benchmark, P2 backpressure 정책 정합성, P3 UDP broker 범위 | 해소/재분류 | P1은 load/open-loop/baseline 으로 진행, P2는 D064/D067, P3는 D060과 UDP broker loopback test 로 닫힘. |
| `2026-06-16-send-queue-high-watermark-impl.md` | high-watermark 구현 승인, last-drop scope 후속 | 해소 | D055 schema 유지, D056 endpoint snapshot, D062 last-drop 미추가 결정. |
| `2026-06-16-interface-server-endpoint-model.md` | endpoint model 방향 승인, 단계적 전환 | 해소/재분류 | D058/D059로 `EndpointId`는 stable routing key 가 아니라 diagnostics id 로 확정. UDP runtime target 은 D060으로 확정. |
| `2026-06-16-endpoint-model-cross-verification.md` | F1 stable endpoint routing 미완, F2 EndpointState 2값, F3 last drop/timestamp | 재분류 | F1은 D058/D059에 따라 v1 범위 밖으로 명확화. F2는 forward-looking 계약으로 유지. F3은 D062로 미추가 결정. |
| `2026-06-17-impl-vs-design-cross-verification.md` | G1 TCP outbound 무프레이밍 | 해소 | D065와 `f316d11`로 TCP subscriber outbound 도 `4-byte big-endian length prefix + payload` 로 전송. |
| `2026-06-18-outbound-framing-and-state.md` | P-Backpressure 실측 stress 필요, O1/O2/L1 후속 | 대부분 해소/비차단 | stalled subscriber stress 와 D066으로 drop-oldest fire/HWM 16을 검증. O1/O2/L1은 현재 영향 없는 후속. |

## 4. 초기 phase 리뷰 현재 상태

| 검토 파일 | 현재 해석 |
|---|---|
| `phase1-bipbuffer.md` | M1/M2 must-fix 는 해소. BipBuffer helper/주석/테스트 보강 완료. |
| `phase1-refcounted-pool.md` | 승인 상태 유지. AddRef/Release/부활 거부/풀 반환 계약은 테스트로 방어됨. |
| `phase2-transport-bipbuffer.md` | 설계 보완 요구는 D007/D016/D017/D045로 반영. SAEA 기준선 direct send 예외는 문서화됨. |
| `phase2-saea-lifecycle.md` | connection tracking 누수 must-fix 는 해소. |
| `phase2-udp-datagram.md` / `phase2-udp-datagram-r2.md` | UDP ownership transfer 와 per-datagram Task.Run 문제는 해소. UDP send queue/drop-oldest 도 적용됨. |
| `phase2-echo-loopback.md` | 승인 상태 유지. 이후 TCP/UDP broker 통합 테스트와 benchmark 로 범위가 확장됨. |
| `phase3-broker-routing.md` | eager cleanup 경합은 NoCleanup 정책으로 해소. 빈 topic entry 누적은 L1 후속으로 유지. |
| `phase3-frame-assembler.md` | C1/C2 통합/정책 후속은 `TcpFrameReceiveHandler`와 close 정책으로 해소. fuzz 보강도 완료. |
| `phase3-frame-adapter-command.md` | O1/O2 close/frame leak 관찰은 해소. |
| `phase3-framing-and-close.md` / `phase3-publish-ownership.md` | D009/D010/D011/D012 계약으로 구현과 테스트에 반영됨. |

## 5. 아직 유효한 비차단 후속

| 우선순위 | 항목 | 현재 위치 |
|---|---|---|
| `P2_LATER` | CI 또는 장기 반복 baseline 을 쌓은 뒤 hard latency SLO threshold 재검토 | `TODOS.md` Deferred Backlog |
| `P3_NICE` | 실제 host/metrics surface 가 생기면 server-level diagnostics model 설계 | `TODOS.md` Deferred Backlog, D068 |
| `P3_NICE` | distinct topic entry 장기 누적에 대한 안전 sweep 여부 | D008 NoCleanup tradeoff, 현재 운영 리스크 낮음 |
| `P3_NICE` | 다중 transport snapshot 병합 시 `EndpointId` namespace 충돌 대응 | D058 진단 전용 전제에서는 현재 영향 없음 |
| `P3_NICE` | `EndpointState.Closing/Faulted` 실제 산출 여부 | forward-looking enum 으로 유지, v1 snapshot 은 Open/Closed 중심 |
| `P3_NICE` | 샘플 기반 수동 fan-out 확인 기록 | 통합 테스트가 동일 경로를 자동 검증하므로 낮은 우선순위 |

## 6. 여전히 범위 밖인 큰 항목

- RIO/io_uring backend 와 OS/capability probe.
- SAEA send/recv path 의 명시적 BipBuffer 최적화 또는 SocketAsyncEventArgs 기반 payload 최적화.
- reliable/durable delivery, reconnect subscription transfer, stable subscriber identity.
- UDP 신뢰성/순서보장/혼잡제어.
- protocol error response/ack.
- configurable pending capacity, per-topic/per-endpoint QoS.
- Markdown report, report history, CI gate.

## 7. 다음 판단

다음 실행 후보는 코드 결함 수정이 아니다. 우선순위는 다음 중 하나다.

1. CI/반복 baseline 확대 설계.
2. Phase 5/6 백엔드 선택/OS capability probe 설계.
3. 샘플 기반 수동 fan-out 실행 기록.

현재 상태에서는 과거 review snapshot 중 "must-fix 때문에 다음 구현을 막는 항목"은 없다.
