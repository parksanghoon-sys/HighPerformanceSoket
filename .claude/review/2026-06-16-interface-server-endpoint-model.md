# 검토: Interface Server endpoint model 설계

- 날짜: 2026-06-16
- 검토자: Claude (계획·검토 담당)
- 대상: `docs/superpowers/specs/2026-06-16-interface-server-endpoint-model-design.md`
- 관련 결정: D053 (goal redefinition), D052 (benchmark JSON report)
- 상태: 방향·시퀀싱 **승인**. 선결 결정 4건 사용자 확정 완료. A(문서 정합화) 반영 완료.

---

## 1. 검토 요약 (한 줄)

설계 문서는 기술적 사실이 정확하고, "관측성 우선 → SLO는 그 소비자" 시퀀싱이 건전하다.
1순위 단위(send-side high-watermark diagnostics)는 작고 위치가 명확해 바로 진행 가능하다.

---

## 2. 코드 대조 결과 (문서 주장 검증)

문서가 주장하는 현재 코드 사실을 실제 소스와 1:1 대조했고, 모두 정확하다.

| 문서 주장 | 실제 코드 | 판정 |
|---|---|---|
| subscription이 `IConnection` 중심 | `src/Hps.Broker/SubscriptionTable.cs:139-143` (참조 동등성 키) | 정확 |
| diagnostics에 queue depth/HWM 없음 | `src/Hps.Transport/Abstractions/TransportDiagnosticsSnapshot.cs:23-33` (drop count만) | 정확 — HWM는 신규 |
| `TrySend(IConnection, TransportSendBuffer)` | `src/Hps.Transport/Abstractions/ITransport.cs:67` | 정확 |
| `TrySendTo(IUdpEndpoint, EndPoint, TransportSendBuffer)` | `src/Hps.Transport/Abstractions/ITransport.cs:76` | 정확 |
| 송신 큐 enqueue/evict 지점 존재, capacity 16 | `src/Hps.Transport/Runtime/TransportConnection.cs:105-119` | 정확 |
| UDP는 broker pub/sub에 미결선 | `TODOS.md` P2_LATER와 일치 | 정확 |

→ 문서의 기술적 전제는 신뢰할 수 있다.

---

## 3. 강점

1. **관측성 우선 시퀀싱이 옳다.** latency SLO gate를 먼저 박지 않고, "어디가 병목인지"를 관측 가능하게
   만든 뒤 SLO를 그 관측값의 소비자로 둔 판단은 건전하다. SLO 기준은 측정 없이 정할 수 없다.
2. **1순위 단위가 작고 위치가 명확하다.** enqueue 경로(`TransportConnection.cs:105`), diagnostics snapshot,
   benchmark report writer(D052)가 모두 이미 존재하므로 확장만 하면 된다.
3. **단계적 마이그레이션**(TCP 유지 → HWM → endpoint id → SubscriptionTable value 교체 → UDP)이
   리뷰 가능한 크기로 잘 쪼개졌다.
4. **범위 밖 목록이 명확하다**(DDS wire/discovery/RTPS/reliable UDP/persistence/TLS/SLO gate).

---

## 4. 선결 결정 — 사용자 확정 완료

### A. 목표 재정의 정합화 → 권위 문서 갱신 (확정·반영 완료)
spec이 프로젝트 목표를 "pub/sub broker"에서 "Interface Server"로 재정의하므로 권위 문서를 정합화한다.
- `DECISIONS.md` D053 — goal-redefinition 기록됨.
- `CURRENT_PLAN.md` — 최종 목표/Phase 4 설명/검증 경로 갱신됨.
- `AGENTS.md:9-12`, `PLAN.md:7-8` — 한 줄 요약을 "Interface Server(내부적으로 pub/sub broker 메커니즘, D053)"로 보강 완료.
- `PLAN.md` Phase 3 백프레셔 기본 정책도 D053에 맞춰 v1 bounded drop-oldest 로 정렬했다. 느린 소비자 disconnect/reject 는
  endpoint QoS 또는 reliable/durable delivery 후속 설계로 둔다.
- 기존 "4096B×100Hz"는 v1 성능 기준선으로 유지.

### B. high-watermark 의미 → transport kind 별 단일 queue 최대 depth (확정)
transport 수명 동안 **어떤 한 TCP connection 또는 UDP endpoint queue 가 도달한 최대 instantaneous pending depth**를 기록한다(합산 아님).
기존 drop counter와 동일한 transport-lifetime 누적이다. 단, endpoint registry 도입 전까지는 "어떤 endpoint가 밀렸나"가 아니라
"TCP/UDP transport kind 중 어느 쪽에서 단일 queue 최대치가 얼마였나"에 답한다.
- **한계 명시 필수**: 큐가 bounded drop-oldest(capacity 16)이므로 HWM ∈ [0,16]으로 **포화**된다.
  HWM=16은 "천장 도달" 신호일 뿐 "얼마나 뒤처졌는지"는 알려주지 못하며, 정량은 drop count와의 조합으로만 해석한다.
  이 상한 의미를 snapshot XML doc과 DECISIONS에 적는다.

### C. overload 시나리오 → 최소 단위 유지 (확정)
1순위는 mechanism + unit test(큐를 capacity까지 강제 충전 시 HWM 증가) + report key까지만 포함한다.
baseline(4096B×100Hz)에서 HWM는 near-0으로 "적체 없음 guard" 역할만 한다.
publisher가 느린 subscriber를 앞지르는 overload benchmark 시나리오는 **별도 후속 단위**로 분리한다.

### D. IngressMessage 모델 (data type id / source id / source timestamp) → 1순위 범위 밖
이는 `TcpCommand`(`SUBSCRIBE`/`PUBLISH <topic> <payload>`) wire/protocol 변경이므로 1순위에 넣지 않는다.
2순위(EndpointId/snapshot) 또는 별도 단위로 둘지는 1순위 완료 후 재판단한다.
spec의 "Source adapter" 절은 장기 모델로 유지하되, "1순위 high-watermark 단위에서는 TcpCommand wire format,
broker publish API, IngressMessage 타입을 변경하지 않는다"는 문구를 명시해야 한다.

---

## 5. 권고 — 1순위 단위: TCP/UDP send queue high-watermark diagnostics

### 대상 파일
- `src/Hps.Transport/Runtime/TransportConnection.cs` — enqueue 경로(`:105-119`)에서 `_pendingSends.Count`의
  단일 connection 최대값을 추적하는 `_pendingSendHighWatermark` 추가.
  동기화는 기존 `_droppedPendingSendCount` 패턴(`:184-191`, `Interlocked`/`Volatile`)을 그대로 따른다.
- `src/Hps.Transport/Saea/SaeaUdpEndpoint.cs` — UDP pending queue에 동일 패턴 적용.
- `src/Hps.Transport/Runtime/TransportBase.cs` — enqueue 직후 계산된 depth 로 TCP/UDP transport-wide **max**를 갱신한다.
  active connection/endpoint 목록을 나중에 훑어 합성하면 닫힌 queue 의 peak 를 잃을 수 있으므로 피한다.
- `src/Hps.Transport/Abstractions/TransportDiagnosticsSnapshot.cs` — TCP/UDP HWM 필드 2개 추가.
  기존 counter 의미 불변 원칙 유지, capacity 상한(B) 의미를 XML doc에 명시.
- `tests/Hps.Benchmarks/TcpLoopbackRunResult.cs` + `TcpLoopbackReportWriter.cs` — report key 추가.
  D052의 "전 runner 동일 key 항상 출력" 규칙 유지(`schema-version` 증가 검토).

### 재사용 (새 public surface 추가 금지)
- 기존 drop counter 추적 패턴(`TransportConnection.cs:184-191`).
- 기존 `ITransportDiagnostics.GetDiagnosticsSnapshot()` capability — 확장만, 신규 인터페이스 없음.

### TDD
- **Red**: `TransportDiagnosticsSnapshot`에 HWM 필드 부재로 transport 단위 테스트가 단언 실패.
- **Green**: 큐를 capacity까지 채운 뒤(느린/막힌 pump 시뮬레이션) snapshot HWM가 도달 depth를 반영.
- **Refactor**: counter update helper 이름이 "단일 connection 최대 depth" 의도를 드러내게 정리.
- **Regression**: `dotnet build HighPerformanceSocket.slnx`, `dotnet test HighPerformanceSocket.slnx`, `git diff --check`.
  benchmark report 3종(`--smoke`/`--load`/`--load-open-loop` + `--report`)에 HWM key가 항상 출력되는지 확인.

---

## 6. 후속 단위 (1순위 이후)

- **2순위**: EndpointId + endpoint snapshot 최소 계약 (SubscriptionTable/broker handler/diagnostics에 영향, 1순위보다 넓음).
- **3순위**: UDP broker v1 wire/control 정책 결정 (TCP control plane vs UDP self-register — 별도 설계 단위).
- IngressMessage 모델(D) 도입 시점 결정.
- overload benchmark 시나리오(C) — HWM가 capacity까지 차는 것을 실측 demonstrate.
- latency SLO 실패 gate — 관측값(HWM/drop/p99 trend)이 쌓인 뒤 판단(D053에서 P2).

---

## 7. 다시 검토할 때 확인할 체크리스트

- [ ] B의 HWM 의미("transport kind 별 단일 queue 최대 depth", endpoint identity 아님, capacity 상한 포화)가 구현·문서에 정확히 반영됐는가.
- [ ] 1순위가 mechanism+unit test+report key로 **최소 범위**를 지켰는가(overload 시나리오 미혼입, C).
- [ ] benchmark report 3종 모두 HWM key를 **항상** 출력하는가(D052 동일 key 규칙).
- [ ] 새 public 인터페이스 없이 기존 `ITransportDiagnostics`만 확장했는가.
- [ ] spec "Source adapter" 절의 시점 모호성(D)이 정리됐는가.
- [ ] PLAN/AGENTS/CURRENT_PLAN/DECISIONS 목표 서술이 D053과 계속 정합한가.
