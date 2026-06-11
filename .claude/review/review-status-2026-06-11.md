# review-status-2026-06-11 — Claude 검토 조치 현황

작성 기준: 2026-06-11 현재 작업 트리.

이 문서는 기존 `.claude/review/*.md` 원문을 삭제하거나 수정하지 않고, Codex가 현재 소스와 상태 문서를 대조해
어떤 검토 의견이 아직 유효한지 정리한 조치 현황이다. 개별 검토서는 작성 당시 커밋/작업 상태의 스냅샷으로 보존한다.

## 1. 현재 기준

- `REVIEW_2026-06-11.md`는 작성 당시에는 맞았지만 현재 상태 판단으로는 오래됐다.
  - 해당 문서의 "실제 소켓 I/O 전무", "Protocol/Broker 미착수" 평가는 현재 작업 트리에는 맞지 않는다.
  - 현재 작업 트리에는 SAEA TCP/UDP loopback, Protocol frame/command, Broker `SubscriptionTable`,
    `BrokerPublisher`까지 존재한다.
- 최신 현재 상태는 `CURRENT_PLAN.md`, `TODOS.md`, `CHANGELOG_AGENT.md`, `DECISIONS.md`와 이 문서를 함께 본다.

## 2. must-fix 조치 현황

| 검토 파일 | 항목 | 현재 상태 | 근거 |
|---|---|---|---|
| `phase1-bipbuffer.md` | M1 write==capacity deadlock | 해소 | `BipBuffer.Commit`에서 capacity 도달 시 즉시 wrap |
| `phase1-bipbuffer.md` | M2 SPSC over-read | 해소 | `GetReadSpan` 반환 길이를 committed count 이하로 제한 |
| `phase2-saea-lifecycle.md` | M1 closed connection tracking leak | 해소 | `TransportConnection` close callback 이 `SaeaTransport.UnregisterConnection` 호출 |
| `phase2-udp-datagram.md` | S1 UDP datagram ownership transfer | 해소 | handler 호출 전 local datagram 참조를 끊어 loop catch 이중 release 방지 |
| `phase2-udp-datagram.md` | S2 UDP send per-datagram Task.Run | 해소 | endpoint pending queue + 단일 UDP send pump |
| `phase3-frame-adapter-command.md` | O1 close notification duplication risk | 해소 | `TcpFrameReceiveHandler`가 connection 별 close 통지를 1회로 제한 |
| `phase3-frame-adapter-command.md` | O2 `OnFrame` 예외 시 frame leak | 해소 | 예외 시 frame release 후 connection close |
| `phase3-broker-routing.md` | R1 eager-cleanup subscriber loss | 해소 | `SubscriptionTable` NoCleanup 정책과 targeted race test |

## 3. 여전히 유효한 비차단 항목

- `P3_NICE`: D010 TCP frame assembler 랜덤 적대적 fuzz 영구 회귀 테스트.
  - 현재 edge 테스트와 결정적 fragmentation fuzz 는 있지만 대량 랜덤 fuzz 는 `TODOS.md` Deferred Backlog 에 남아 있다.
- `P1_SOON`: UDP receive backpressure 정책.
  - UDP send 직렬화는 끝났지만, handler/fan-out 이 느릴 때 receive side pool growth 를 제한할 정책은 아직 별도 설계가 필요하다.
- `P2_LATER`: 4096 bytes x 100 Hz 목표의 Phase 4 벤치마크 기준화.
  - 아직 p50/p99, 큐 적체 허용치, fan-out 배율이 성능 게이트로 고정되지 않았다.

## 4. 범위 밖 또는 후속 작업

- Broker command handler, protocol error 응답, Server wiring, samples 는 아직 후속 단위다.
- drop-oldest backpressure 구현은 D012 결정만 존재하고 실제 Broker/Transport 정책 구현은 후속이다.
- RIO/io_uring backend 와 실제 OS capability probe 는 Phase 5/6 범위다.

## 5. 보존 원칙

- 기존 검토서는 당시 판단 근거와 리뷰 이력을 남기기 위해 삭제하지 않는다.
- 현재 구현 상태와 어긋난 오래된 평가는 이 문서에서 superseded 로 해석한다.
- 새 Claude 재검토가 들어오면 이 파일을 수정하기보다 별도 `review-status-YYYY-MM-DD-rN.md` 또는 phase별 rN 검토서를 추가한다.
