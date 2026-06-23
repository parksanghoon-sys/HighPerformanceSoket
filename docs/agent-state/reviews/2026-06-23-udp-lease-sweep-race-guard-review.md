# 2026-06-23 UDP lease sweep registry race guard 리뷰

## 1. Scope

- 검토 대상: `a817c6e` UDP lease sweep registry cleanup race guard 구현.
- 핵심 목적: lease sweep 이 expired target snapshot 을 만든 뒤 같은 UDP stable target 이 재등록될 때, stale registry cleanup 이 새 online 상태를 disconnected 로 덮지 못하게 막았는지 확인한다.
- 범위 밖: benchmark schema, latency hard gate, RIO/io_uring backend, stable identity 인증/권한, payload replay, diagnostics friendly-name 노출.

## 2. Findings

Blocker/Major correctness finding 은 발견하지 못했다. D077의 handler gate 직렬화는 코드와 테스트에 일관되게 반영되어 있다.

### Finding 1

- **Severity**: Minor
- **Dimension**: testing / maintainability
- **Evidence**
  - race regression test 는 구 구현을 더 잘 노출하기 위해 `remoteEndPoint.WaitForAccess(TimeSpan.FromMilliseconds(250))`로 REGISTER task 에 짧은 실행 기회를 준다.
  - 해당 반환값은 assertion 으로 쓰지 않고, 주석도 수정 구현에서는 handler gate 에 막혀 access signal 이 오지 않을 수 있음을 명시한다.
- **Impact**
  - 현재 수정 구현의 green 판단은 이 250ms 반환값에 의존하지 않는다.
  - 다만 구 구현의 red signal 을 재현하는 방식은 scheduler timing 에 일부 기대고 있으므로, 매우 느린 환경이나 내부 Dictionary 비교 순서가 바뀌는 경우 red 재현성이 약해질 수 있다.
- **Recommendation**
  - 이번 단위의 must-fix 는 아니다. test comment 가 의도를 충분히 설명하고 있고, fixed path 에서는 deterministic 하게 sweep/register 직렬화를 검증한다.
  - 같은 유형의 race test 가 늘어나거나 flake 가 관측되면 그때 tracker/registry 경계에 test-only synchronization seam 을 별도 설계로 도입한다.

## 3. Material failure modes

### Sweep/Re-register stale cleanup

- **Trigger**: lease sweep 이 expired target snapshot 을 만든 뒤 registry cleanup 전에 같은 UDP remote 가 같은 stable identity 로 다시 `REGISTER`한다.
- **Impact**: 이전 구현에서는 새 online 상태가 stale `RemoveTarget(...)` 호출에 의해 disconnected 로 되돌아갈 수 있었다.
- **Detection**: `SweepExpiredUdpLeases_WhenRegisteredRemoteReRegistersDuringSweep_KeepsReRegisteredTargetOnline`가 기존 구현에서 `Assert.True()` failure 를 냈고, 수정 후 같은 stable target 이 subscribed 상태로 남는지 확인한다.
- **Mitigation**: `BrokerUdpDatagramHandler`의 `_gate`가 UDP receive command, endpoint close cleanup, lease sweep state mutation 을 직렬화한다. sweep 은 expired snapshot cleanup 까지 같은 gate 안에서 끝내므로 REGISTER 가 중간에 끼어들 수 없다.

### PUBLISH fan-out lock expansion

- **Trigger**: UDP `PUBLISH`가 handler gate 안에서 실제 fan-out 까지 수행되면 느린 subscriber send path 가 다른 UDP command 처리를 막을 수 있다.
- **Impact**: shared UDP endpoint 에서 unrelated remote 의 REGISTER/SUBSCRIBE/UNSUBSCRIBE 처리가 불필요하게 지연될 수 있다.
- **Detection**: D077과 `BrokerUdpDatagramHandler.OnDatagramReceived(...)`에서 lease activity 갱신만 gate 안에서 수행하고 `_publisher.Publish(...)`는 lock 밖에서 호출하는지 확인했다.
- **Mitigation**: 이번 구현은 topic/offset/length 만 gate 안에서 캡처하고 실제 fan-out 은 lock 밖에서 수행한다.

## 4. Deferred items

- race test 가 더 늘어나면 scheduler timing 대신 명시적 test synchronization seam 을 둘지 검토한다. 현재는 단일 회귀 테스트이고 비차단이다.
- lease tracker 와 stable registry 의 owner 모델이 더 복잡해지면, D077 handler gate 를 유지할지 또는 단일 registry/lease owner 로 합칠지 별도 설계가 필요할 수 있다.

## 5. Unresolved decisions that may bite you later

- 현재 단위 기준으로 다음 구현을 막는 미해결 결정은 없다.
- 장기적으로는 stable identity registry 와 UDP lease tracker 를 계속 분리할지, 하나의 broker state owner 로 합칠지 결정해야 할 수 있다. 이번 race 는 두 owner 사이 linearization point 가 명확하지 않아 발생한 계열의 문제다.

## 6. Completion summary

- Reviewed scope: `a817c6e`의 UDP lease sweep registry race guard, D077 문서, handler/test 경계.
- Major findings: 없음.
- Key risks: fixed path 는 handler gate 로 닫혔다. 남은 리스크는 race test 의 red 재현 방식이 scheduler timing 을 일부 활용한다는 비차단 관찰이다.
- Deferred items: test synchronization seam 검토, 장기 owner 모델 정리.
- Unresolved important decisions: 현재 구현을 막는 항목 없음.
