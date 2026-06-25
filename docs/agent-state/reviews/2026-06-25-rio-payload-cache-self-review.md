# 2026-06-25 RIO payload registration cache self-review

## 1. Scope

- 검토 대상: D107 `RioPayloadRegistrationCache` 구현, RIO payload send path cache lease 연결, 관련 tests/spec/plan/state 문서.
- 핵심 목적: RIO payload send 경로의 per-operation `RIORegisterBuffer`/`RIODeregisterBuffer` 비용을 connection-local bounded cache 로 줄이면서, outstanding send 중 native buffer id 를 조기 해제하지 않는지 확인한다.
- 범위 밖: transport-wide shared cache, RIO UDP, `TransportFactory` 기본 backend 승격, latency SLO hard gate.

## 2. Findings

### Finding 1

- **Severity**: Minor
- **Dimension**: maintainability / performance
- **Evidence**: self-review 중 `RioPayloadRegistrationCache.TryEvictIdleEntry(...)`가 idle entry 를 `_entries`에서 제거한 뒤 `_registrar.Deregister(...)`를 `_gate` lock 내부에서 호출하는 것을 확인했다.
- **Impact**: idle entry 만 대상으로 하므로 correctness 문제는 없지만, native deregister 호출이 lock 내부에 남아 D107 설계의 "cache 상태 변경은 lock 내부, native release 는 가능한 lock 밖" 의도와 어긋났다. OS 호출 지연 시 같은 connection cache 의 acquire/release 관측이 불필요하게 대기할 수 있다.
- **Recommendation**: 정상 eviction 경로는 buffer id 만 수집하고 lock 밖에서 deregister 한다. 새 registration 실패 경로에서는 이미 cache 에서 제거한 idle registration 이 누수되지 않도록 예외 경로에서 정리한다.
- **Status**: 반영 완료. `Acquire(...)`가 evicted buffer id 를 lock 밖에서 deregister 하도록 리팩터했고, register 실패 예외 경로에는 unreachable native registration 방지 정리를 추가했다.

## 3. Material failure modes

### Native registration lifetime

- **Trigger**: cached payload buffer 를 보내는 도중 connection close/dispose 가 cache 를 dispose 한다.
- **Impact**: outstanding RIO request 가 완료되기 전에 buffer id 를 deregister 하면 native request 가 invalid buffer id 를 참조할 수 있다.
- **Detection**: `Dispose_WhenLeaseIsOutstanding_DeregistersAfterLeaseRelease` owner test 와 RIO close/handler close 반복 테스트.
- **Mitigation**: cache dispose 는 outstanding entry 를 `DeregisterWhenIdle`로 표시하고, 마지막 lease release 에서 deregister 한다.

### Cache capacity exhaustion

- **Trigger**: connection-local cache capacity 64 를 초과하고 모든 entry 가 outstanding 상태다.
- **Impact**: idle entry 를 찾지 못한 상태에서 강제 eviction 하면 outstanding send 의 native buffer id 를 조기 해제할 수 있다.
- **Detection**: `Acquire_WhenCapacityIsFullAndAllEntriesAreOutstanding_UsesUncachedLease` owner test.
- **Mitigation**: all-outstanding 상태에서는 cache 에 넣지 않는 fallback lease 를 발급하고 lease release 즉시 deregister 한다.

### Registration failure after idle eviction

- **Trigger**: idle entry 를 cache 에서 제거한 직후 새 `RIORegisterBuffer` 호출이 실패한다.
- **Impact**: 제거한 idle entry 의 native registration 이 dictionary 에서 사라진 채 남을 수 있다.
- **Detection**: self-review 로 발견한 예외 경계.
- **Mitigation**: 새 register 실패 catch 에서 evicted buffer id 를 즉시 deregister 하고 예외를 다시 던진다.

## 4. Deferred items

- transport-wide shared payload registration cache 는 deferred 유지. 현재 connection-local cache 는 owner/close 경계가 단순하고, fan-out 시 동일 payload 를 여러 connection 으로 보낼 때 connection 별 1회 registration 은 남는다. fan-out benchmark evidence 가 쌓인 뒤 별도 설계로 승격한다.
- cache capacity 64 는 D107의 bounded default 로 유지한다. 실제 workload 에서 capacity pressure metric 이 필요해지면 diagnostics 설계와 함께 다룬다.
- RIO backend 를 default factory 후보로 올리기 전, capability probe/fallback/contract parity/readiness gate 를 별도 설계로 닫는다.

## 5. Unresolved decisions that may bite you later

- RIO를 언제 `TransportFactory` 기본 backend 후보로 승격할지 아직 결정되지 않았다. 현재는 opt-in/test path 로 검증 중이며, fallback 정책과 parity checklist 가 먼저 필요하다.
- transport-wide payload cache 를 도입할지 여부는 아직 open 이다. 도입하면 fan-out 중 native registration 수는 더 줄어들 수 있지만, pool block lifetime 과 cross-connection lease owner 가 훨씬 복잡해진다.

## 6. Completion summary

- Reviewed scope: D107 payload registration cache owner, RIO send path wiring, owner/loopback tests, D107 spec/plan.
- Major findings: 없음.
- Key risks: outstanding send 중 deregister, all-outstanding capacity pressure, register failure after eviction 를 확인했고 현재 구현/리팩터로 대응했다.
- Deferred items: transport-wide shared cache, cache capacity diagnostics, RIO default factory readiness.
- Unresolved important decisions: RIO 기본 backend 승격 기준, transport-wide payload cache 여부.

## Verification

- `dotnet test tests\Hps.Transport.Rio.Tests\Hps.Transport.Rio.Tests.csproj --no-restore --filter "FullyQualifiedName~RioPayloadRegistrationCacheTests"`: 4개 통과.
- `dotnet test tests\Hps.Transport.Rio.Tests\Hps.Transport.Rio.Tests.csproj --no-restore`: 34개 통과.
- `dotnet test tests\Hps.Transport.Tests\Hps.Transport.Tests.csproj --no-restore --filter "FullyQualifiedName~Close|FullyQualifiedName~Wake|FullyQualifiedName~Pending"`: 19개 통과.
- RIO close/handler close tests 2개를 10회 반복 실행: 모두 통과.
- `git diff --check`: whitespace error 없음.
