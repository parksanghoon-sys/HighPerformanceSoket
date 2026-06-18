# 교차검증: 현재 구현 상태 ↔ Interface Server endpoint 설계

- 날짜: 2026-06-16
- 검토자: Claude (계획·검토 담당)
- 검증 HEAD: `a5b08d5` (route broker subscribers through endpoint targets)
- 누적 대상 커밋: `22591b5`/`db8984f`(high-watermark) → `02a8eb1`/`f77344b`(endpoint snapshot) → `a5b08d5`(broker endpoint routing)
- 설계 근거:
  - spec `docs/superpowers/specs/2026-06-16-interface-server-endpoint-model-design.md`
    (Endpoint registry, Subscription index 단계적 전환, 송신 측 관측성)
  - 검토 `.claude/review/2026-06-16-interface-server-endpoint-model.md`
- 관련 결정: D053, D055, D056
- 상태: **설계 방향과 정합**. 단, 핵심 목표(endpoint identity 기반 라우팅)는 아직 미완 — 단계적 전환 진행 중.

---

## 1. 한 줄 결론

구현은 spec의 단계적 전환 순서(1 high-watermark → 2 endpoint snapshot → 3 SubscriptionTable value 분리)를 정확히 따라
진행 중이다. 1·2단계는 설계 모델과 일치하게 완료됐고, 3단계는 **값 추상화 경계까지만** 들어갔다(라우팅 키는 아직
connection reference, EndpointId 아님). 빌드 0/0, 전체 테스트 green.

## 2. 단계별 진행도 (spec "Subscription index 단계적 전환")

| 단계 | spec 의도 | 구현 | 상태 |
|---|---|---|---|
| 1 | send queue high-watermark 먼저 | TransportBase transport-wide max + benchmark report | ✅ 완료 |
| 2 | EndpointId + endpoint snapshot 모델 도입 | EndpointId/Kind/State, EndpointSnapshot, ITransportEndpointDiagnostics, per-connection 추적 | ✅ 완료 |
| 3 | SubscriptionTable value 를 endpoint 중심으로 | BrokerSubscriber 값 타입으로 경계 분리, SubscriptionTable value 교체 | ⚠️ 부분 (아래 §4) |
| 4 | UDP endpoint subscription 결선 | 미착수 (BrokerSubscriber 에 UDP target 자리만 예약) | ⏳ 후속 |

## 3. spec "Endpoint registry" 모델 ↔ 구현 대조

| spec 필드 | 구현 | 일치 |
|---|---|---|
| endpoint id | `EndpointId` (양수 long, handle 참조와 분리) | ✅ |
| transport kind TCP/UDP | `EndpointTransportKind` | ✅ |
| current transport handle | snapshot 에서 의도적으로 제외(값 전용, handle 누수 방지) | ✅ 설계 근거대로 |
| state: Open/Closing/Closed/Faulted | `EndpointState` enum 4값 정의 | ✅ 정의됨 / ⚠️ 실제로는 Open·Closed 만 산출(§5) |
| diag: pending send count | `EndpointSnapshot.PendingSendCount` | ✅ |
| diag: pending high-watermark | `PendingSendQueueHighWatermark` (per-endpoint) | ✅ |
| diag: dropped count | `DroppedPendingSendCount` | ✅ |
| diag: last drop reason | — | ❌ 미구현 |
| diag: last send timestamp | — | ❌ 미구현 |

## 4. 핵심 교차검증 발견 (사용자가 알아야 할 것)

### F1 — EndpointId 는 아직 "진단 전용"이며 라우팅 키가 아니다 (가장 중요)
- spec line 15 핵심 목표: "각 subscription 은 connection 객체가 아니라 안정적인 endpoint identity 를 기준으로 관리".
- 현재 `BrokerSubscriber`(라우팅 값)는 `EndpointId` 를 담지 않고 **TCP `IConnection` reference identity** 로 비교한다
  (`BrokerSubscriber.Equals` → `ReferenceEquals(_tcpConnection, ...)`). `ForTcp` doc 도 "stable id 바인딩은 후속 단계"라고 명시.
- 따라서 **reconnect 를 같은 endpoint 로 보는 설계 약속은 아직 실현되지 않았다.** EndpointId 는 현재 EndpointSnapshot(진단)에만 쓰인다.
- 판정: 단계적 전환상 정상(3단계 미완). 다만 "endpoint 중심 broker" 헤드라인 목표는 미달 상태임을 분명히 한다.
  3단계 완료 = `SubscriptionTable`/`BrokerSubscriber` 가 EndpointId 로 keying 되고 reconnect 재바인딩이 동작할 때.

### F2 — EndpointState 는 4값 정의되었으나 Open/Closed 만 산출된다
- `TransportConnection.CreateSnapshot`: `state = _closed ? Closed : Open`. **Closing/Faulted 는 어디서도 set 되지 않는다.**
- 즉 graceful closing 중간 상태나 handler 예외/socket error 로 인한 Faulted 를 snapshot 으로 구분할 수 없다.
- 판정: forward-looking 계약으로 enum 은 맞지만, 현재 관측값은 2상태뿐. spec 의 4상태 모델 대비 부분 구현.

### F3 — spec diagnostics 중 last drop reason / last send timestamp 미구현
- §3 표의 마지막 두 항목. last drop reason 은 이전 검토 갭 1(마지막 drop scope)과 겹친다.
- 판정: 합리적 deferral 이나, endpoint snapshot 이 spec diagnostics 목록의 부분집합임을 명시.

### F4 — HWM 추적이 2계통으로 공존 (정상, 아키텍처 메모)
- transport-wide max(`TransportBase`, 1단계, lock-free CAS, benchmark report 소비) +
  per-endpoint max(`TransportConnection._pendingSendQueueHighWatermark`, 2단계, `_gate` 내부 갱신, EndpointSnapshot 소비).
- 둘은 서로 다른 소비자를 위한 별개 값이며 모두 정확. benchmark report 는 여전히 transport-wide 를 쓴다.

## 5. 설계와 정합한 강점

- **값 전용 snapshot**: socket/connection/buffer handle 미포함 → 닫힌 연결 누수/수명 우회 방지. spec 근거와 정확히 일치.
- **단계 순서 준수**: 1→2→3 을 spec 권장 "안전한 전환 순서" 그대로 진행.
- **BrokerSubscriber 경계 분리**: UDP wire format 을 확정하지 않고 같은 값 모델에 UDP target 자리만 예약 → spec "UDP 는 후속 단위" 의도와 일치.
- **EndpointId 불변식**: 0 이하 거부, handle 참조와 분리 → spec "connection 객체에만 의존하지 않는다" 충족(라우팅 결선만 남음).

## 6. 검증 (직접 실행, HEAD a5b08d5)

- `dotnet build HighPerformanceSocket.slnx`: 경고 0, 오류 0.
- `dotnet test HighPerformanceSocket.slnx`: 실패 0
  (Hps.Protocol 28 / Hps.Broker 20 / Hps.Server 5 / Hps.Transport 43 / Hps.Buffers 18).
- endpoint 관련 focused (`~Endpoint`): 7건 통과, 실패 0 — endpoint snapshot 신규 테스트가 실제로 발견·실행됨을 확인.

## 7. 권고

1. **F1 을 명시적 다음 단위로**: `SubscriptionTable`/`BrokerSubscriber` 가 EndpointId 로 keying 하고 reconnect 재바인딩을
   처리하는 3단계 완료를 별도 작업 단위로 세운다. 이게 "endpoint 중심 broker" 목표의 실질 완료 지점이다.
2. **F2 결정**: Closing/Faulted 를 실제로 산출할지(close drain 시작 시 Closing, handler 예외/socket error 시 Faulted),
   아니면 v1 은 Open/Closed 2상태로 충분하다고 보고 enum 주석에 "Closing/Faulted 는 후속" 으로 명시할지 결정한다.
3. **F3 위치 확정**: last drop reason/last send timestamp 를 EndpointSnapshot 필드로 둘지, transport aggregate 로 둘지
   범위를 정해 TODOS 에 기록(이전 갭 1 과 통합).

## 8. 다시 검증할 때 체크리스트

- [ ] F1: SubscriptionTable/BrokerSubscriber 가 EndpointId 로 keying 되고 reconnect 재바인딩이 테스트로 검증되는가.
- [ ] F2: EndpointState 4값이 실제로 산출되는가, 아니면 2상태 한정이 문서화됐는가.
- [ ] F3: last drop reason/last send timestamp 의 위치/범위가 결정·기록됐는가.
- [ ] 전체 테스트 실패 0, endpoint focused 통과 유지.
