# 검토: send queue high-watermark diagnostics 구현 (설계 대비)

- 날짜: 2026-06-16
- 검토자: Claude (계획·검토 담당)
- 대상 커밋: `22591b5` (track), `db8984f` (report), `7eabb3e` (state follow-up), `f77344b` (endpoint snapshot follow-up)
- 설계 근거:
  - spec `docs/superpowers/specs/2026-06-16-interface-server-endpoint-model-design.md` (1순위 "송신 측 관측성")
  - 구현 계획 `docs/superpowers/plans/2026-06-16-send-queue-high-watermark-diagnostics.md` (Task 1/2)
  - 검토 `.claude/review/2026-06-16-interface-server-endpoint-model.md` (결정 B/C/D)
- 관련 결정: D053, D055, D056
- 상태: **승인**. 설계-구현 정합 확인. 재검토(deeper pass)에서 솔루션 전체 테스트·테스트 품질까지 확인.
  schema-version 결정과 endpoint snapshot follow-up 은 반영됐고, 후속 판단은 마지막 drop scope 1건만 남음.

---

## 1. 한 줄 결론

구현은 설계(spec 1순위 + 구현 계획)에 충실하며, 의미론·구현 방식·검증 범위가 설계와 정확히 일치한다.
빌드 0/0, 솔루션 전체 테스트 112건 통과(실패 0), benchmark report에 HWM key 출력까지 end-to-end 검증됨.
신규 테스트 3건은 성장·수명 보존·누수 0 을 결정적으로 검증한다.

---

## 2. spec 1순위 "송신 측 관측성" 요구 → 구현 대조

| 설계(spec) 요구 | 구현 | 일치 |
|---|---|---|
| TCP HWM = 수명 동안 단일 TCP connection 이 도달한 최대 pending depth | `TransportBase._tcpPendingSendQueueHighWatermark` + `UpdateMax`, enqueue 직후 depth 보고 | 정확 (합산 아님) |
| UDP HWM = 단일 UDP endpoint queue 최대 pending depth | `_udpPendingSendQueueHighWatermark`, `SaeaUdpEndpoint`→`_transport.RecordUdpPendingSendDepth` | 정확 |
| 누적 dropped count | 기존 TCP/UDP drop counter 유지 | 기구현 |
| enqueue 직후 depth 로 transport-wide max 갱신, 닫힌 뒤 보존 | lock 안 캡처 → lock 밖 CAS max, close 후 값 유지 | 설계 명시 방식 그대로 |
| HWM 는 capacity 16 에서 포화, drop count 와 함께 해석 | snapshot XML doc + DECISIONS D053 명시, full 시 count=capacity 유지 | 정확 |
| (가능하면) 마지막 drop 이 발생한 transport kind/endpoint 범위 | 미구현 | 갭 1 (아래) |

## 3. 구현 계획(Task 1/2) → 구현 대조

| 계획 단위 | 구현 | 일치 |
|---|---|---|
| Task 1: snapshot 4-인자 ctor + 2 property | 반영 | 일치 |
| Task 1: `TransportBase` max 필드 + `UpdateMax` + snapshot 연결 | 반영 | 일치 |
| Task 1: `TransportConnection` depth callback / SAEA TCP·UDP 결선 | 반영 | 일치 |
| Task 1: contract + TCP + UDP 테스트 (+2) | 반영, 39 통과 | 일치 |
| Task 2: `RunResult`/`Print` + `ScenarioRunner` + `ReportWriter` key | 반영, stdout/JSON 무조건 출력 | 일치 |

## 4. 구현 정확성 (직접 검증)

- `UpdateMax`: lock-free CAS max (Volatile.Read → 비교 → CompareExchange 재시도). 올바름.
- depth 보고 경계: `_gate`/`_sendGate` 안에서 depth 캡처, 보고는 lock 밖. monotonic max 라 보고 순서가 뒤바뀌어도 손실 없음.
- HWM 상한: full 일 때 dequeue→enqueue 로 count 가 capacity(16)에 머물러 포화 — 문서와 일치, off-by-one 없음.
- peak 측정 시점: dequeue 는 depth 를 줄이고 peak 는 enqueue 직후 발생하므로, enqueue 시점 측정이 최대값을 정확히 포착(under-report 없음).

## 5. 구현이 계획보다 나은 점

구현 계획 원본은 `TransportConnection` 생성자 오버로드가 불완전해(4-인자 `(.., Action?, Action<int>?)` 부재)
`CreateConnection`/`CreateSocketConnection` 호출이 **컴파일되지 않는 상태**였다. 구현은 그 4-인자 오버로드를 추가하고
전 생성자를 단일 5-인자 초기화로 위임해 바로잡았다. 즉 구현이 계획의 결함을 교정했다.

## 6. 설계 대비 갭 (2건, 모두 블로커 아님)

### 갭 1 — spec의 optional 관측값 "마지막 drop transport kind/endpoint 범위" 미구현
- spec은 "**가능하면**"(optional)으로 기재, 구현 계획은 범위 제외, 구현도 따랐다.
- drop count 가 TCP/UDP 로 분리돼 어느 kind 에서 drop 났는지는 추론 가능하므로 합리적 deferral.
- spec minimum-observables 목록 중 유일하게 구현으로 넘어오지 않은 항목이라 명시해 둔다.
- 현재 상태: `TODOS.md`에 별도 `P2_LATER` 항목으로 기록됐다. EndpointId lifecycle/snapshot collection 은 `f77344b`에서 완료됐으므로,
  남은 판단은 마지막 drop 을 endpoint snapshot 필드로 둘지, transport aggregate 필드로 둘지의 범위 결정이다.

### 갭 2 — `schema-version` 미증가
- HWM key 2개를 추가했으나 `schema-version`은 1 유지(additive 선택).
- 설계/계획이 버전 증가를 명시 요구하지 않았으므로 위반은 아니다.
- 현재 상태: D055와 `CHANGELOG_AGENT.md`에 "additive field 이므로 version 1 유지" 근거가 기록됐다.

## 7. 검증 결과 (직접 실행)

- `dotnet build HighPerformanceSocket.slnx`: 경고 0, 오류 0.
- **솔루션 전체 테스트 `dotnet test HighPerformanceSocket.slnx`: 실패 0.**
  - Hps.Buffers.Tests 18 / Hps.Transport.Tests 43 / Hps.Protocol.Tests 28 / Hps.Broker.Tests 18 / Hps.Server.Tests 5.
- smoke `--report`: stdout/JSON 모두 `tcp/udp-pending-send-queue-high-watermark` 출력,
  TCP HWM=1, UDP=0, dropped 0, pool-rented 0, `smoke-result: pass`.
- smoke TCP HWM=1 은 closed-loop 라 큐가 쌓이지 않는 것 → spec/검토 C의 "baseline near-0 guard" 예측과 일치.

## 7-1. 재검토 (deeper pass) — 추가 테스트 품질

이번 단위의 신규 테스트 3건이 의도를 실제로 검증하는지 코드를 직접 확인했다. 모두 vacuous 하지 않고 결정적이다.

- **contract 테스트** (`TransportDiagnostics_..._UsesOptionalCapabilityWithoutExpandingITransport`):
  reflection 으로 새 property 2개의 존재·타입(int), 4-인자 constructor 존재를 단언하고,
  `(2L,3L,4,5)` 로 생성해 tcp=4/udp=5, 기존 2-인자 snapshot 은 0/0 임을 확인한다. 계약을 실제로 검증.
- **TCP 테스트** (`TrySend_WhenPendingQueueGrows_...`): capacity 16 미만(5건)으로 evict 없이 채운 뒤
  HWM=5·UDP=0·dropped=0 을 확인하고, release + `Close()` 후에도 HWM=5 가 **유지**되며 `pool.RentedCount==0`
  (누수 0)을 확인한다. 성장·수명 보존·누수 0 을 한 번에 검증.
- **UDP 테스트** (`UdpSendTo_WhenPendingQueueGrows_...`): 실제 `SaeaTransport` + UDP socket 으로 대칭 검증.
  - **결정성 확인**: 이 테스트는 `SaeaUdpEndpoint` 를 직접 생성하고 `StartAsync`/`BindUdpAsync` 를 거치지 않으므로
    send pump 가 큐를 drain 하지 않는다. 따라서 5건이 모두 큐에 남아 HWM=5 가 결정적으로 나온다. 이는 기존
    `UdpSendTo_WhenPendingQueueDropsOldest_...`(17건으로 eviction 유발) 가 이미 의존하는 동일 패턴이라
    flaky 위험이 없다.

→ 테스트는 mechanism(성장), lifetime 보존(close 후 유지), 누수 0 을 모두 덮으며, 검토 C의 "최소 단위" 의도와 일치한다.

## 8. 현재 반영 상태 (참고)

- high-watermark docs follow-up 은 `7eabb3e`로 커밋됐다.
- EndpointId runtime wiring 과 endpoint snapshot collection 은 `f77344b`로 커밋됐다.
- 현재 남은 판단은 last-drop scope 뿐이다. `AGENTS.md`, `PLAN.md`, spec, 다른 `.claude/review/*` 파일의 미커밋 상태는
  이 high-watermark 구현 검토의 블로커가 아니며, 별도 범위에서 다룬다.

## 9. 다시 검토할 때 확인할 체크리스트

- [x] 갭 1(마지막 drop scope) 후속 위치가 TODOS에 기록됐는가 — `P2_LATER` 항목으로 기록됨.
- [x] 갭 2(schema-version) 결정과 근거가 D055/CHANGELOG에 남았는가 — D055와 changelog에 기록됨.
- [x] 상태 문서 docs 커밋이 이번 단위 범위만 포함하는가 — `7eabb3e`가 high-watermark review follow-up docs 전용 커밋.
- [x] EndpointId runtime wiring 이 반영됐는가 — `f77344b`에서 `ITransportEndpointDiagnostics`와 active snapshot collection 구현.
- [x] 솔루션 전체 테스트(`dotnet test HighPerformanceSocket.slnx`)가 실패 0인가 — 재검토에서 확인(112건 통과, 실패 0).
- [x] 신규 테스트 3건이 vacuous 하지 않고 결정적인가 — 재검토에서 확인(§7-1).
- [x] Codex 재확인: high-watermark 3건 + endpoint snapshot 3건 focused 테스트 통과(6건, 실패 0).
