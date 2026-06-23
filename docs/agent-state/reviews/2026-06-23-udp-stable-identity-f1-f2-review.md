# 2026-06-23 UDP stable identity F1/F2 수정 리뷰

## 1. Scope

- 검토 대상: `b85220f` UDP stable identity lease sweep registry cleanup, `8749c64` UDP invalid stable identity datagram isolation.
- 핵심 목적: 2026-06-23 stable subscriber identity 교차검증에서 나온 UDP must-fix 2건이 실제 수명/소유권 경계와 맞게 닫혔는지 확인한다.
- 범위 밖: 새로운 기능 구현, benchmark/diagnostics surface 변경, stable identity 인증/권한, persistence, payload replay.

## 2. Findings

### Finding 1

- **Severity**: Major
- **Dimension**: correctness / reliability / concurrency
- **Evidence**
  - `BrokerServer`의 UDP lease sweep timer callback 은 receive loop 와 별도 callback 경로에서 `SweepExpiredUdpLeases(...)`를 호출한다: `src/Hps.Server/BrokerServer.cs:390-392`.
  - `BrokerUdpDatagramHandler.SweepExpiredUdpLeases(...)`는 `_udpLeases.SweepExpired(now, expiredTargets)`로 lease/routing 을 먼저 정리한 뒤, 별도 loop 에서 `_subscriberRegistry.RemoveTarget(...)`을 호출한다: `src/Hps.Broker/BrokerUdpDatagramHandler.cs:162-173`.
  - `UdpRemoteLeaseTracker.SweepExpired(...)`는 lease lock 안에서 expired target snapshot 을 만든 뒤 lock 을 반환한다: `src/Hps.Broker/UdpRemoteLeaseTracker.cs:159-184`.
  - UDP `REGISTER` datagram 은 receive path 에서 같은 handler 의 `RegisterUdpTarget(...)`로 들어오며, sweep callback 과 같은 broker-level lock 으로 직렬화되지 않는다: `src/Hps.Broker/BrokerUdpDatagramHandler.cs:72-100`, `src/Hps.Broker/BrokerUdpDatagramHandler.cs:178`.
- **Impact**
  - 만료 sweep 이 expired target snapshot 을 만든 직후, 같은 UDP remote 가 같은 stable identity 로 다시 `REGISTER`하면 registry 는 다시 online/current target 으로 바뀔 수 있다.
  - 그 다음 sweep callback 의 stale `_subscriberRegistry.RemoveTarget(expiredTarget, now)`가 실행되면, 방금 재등록된 동일 target 을 disconnected 상태로 되돌릴 수 있다.
  - 결과적으로 활성 UDP subscriber 가 재등록했는데도 retained topic routing 이 끊기고, retention sweep 이 metadata 를 제거할 수 있다.
- **Recommendation**
  - 다음 구현 단위에서 sweep registry cleanup 을 조건부로 만들거나, lease sweep 과 registry target disconnect 를 같은 선형화 지점으로 수렴시킨다.
  - 최소 수정 후보는 `UdpRemoteLeaseTracker`가 expired target 과 함께 sweep 당시 lease generation 또는 last-seen snapshot 을 반환하고, `BrokerUdpDatagramHandler`가 registry disconnect 전에 해당 target 에 더 최신 lease 가 생기지 않았는지 확인하는 방식이다.
  - 대안은 broker-level lock 으로 UDP `REGISTER`/lease sweep/registry cleanup 을 함께 직렬화하는 것이지만, 기존 `SubscriptionTable`/`SubscriberRegistry` lock 순서와 교착 가능성을 먼저 검토해야 한다.

## 3. Material failure modes

### Sweep/Re-register race

- **Trigger**: UDP lease timer 가 expired target snapshot 을 만든 직후, 같은 endpoint/remote 가 같은 identity 로 `REGISTER`를 다시 보낸다.
- **Impact**: active target 이 registry 에서 disconnected 로 표시되고 retained topic fan-out 이 끊긴다.
- **Detection**: 단위 테스트에서는 timer와 datagram path 를 직접 interleave 해야 잡힌다. 현재 `SweepExpiredUdpLeases_WhenRegisteredRemoteExpires_MarksRegistryTargetDisconnected`는 순차 경로만 검증한다.
- **Mitigation**: next unit 에서 deterministic fake hook 또는 tracker-level generation check 를 추가해 stale cleanup 이 새 activity 를 덮지 못하게 한다.

## 4. Deferred items

- F2 invalid identity datagram isolation 은 현재 범위에서 적절하다. TCP invalid identity 는 connection-local protocol error 로 close 되는 정책이고, UDP는 shared endpoint 라서 datagram drop 으로 격리하는 비대칭이 타당하다.
- Protocol decoder 의 whitespace grammar 를 더 엄격히 바꾸는 작업은 이번 must-fix 와 별개다. 현재 UDP handler boundary 에서 endpoint close 를 막는 목적은 달성했다.

## 5. Unresolved decisions that may bite you later

- UDP lease tracker 와 stable registry 를 장기적으로 하나의 수명 모델로 합칠지, 지금처럼 lease tracker 는 routing/idle owner 이고 registry 는 stable identity owner 로 분리할지 결정이 필요하다. 이번 finding 은 두 owner 사이의 선형화 지점이 명확하지 않아 생긴 문제다.

## 6. Completion summary

- Reviewed scope: F1/F2 UDP stable identity must-fix 커밋 2개와 관련 Broker/Server 경계.
- Major findings: F1 sweep registry cleanup 에 stale target snapshot 경쟁 창이 남아 있다.
- Key risks: lease sweep callback 과 UDP receive callback 이 같은 broker-level sequencing 없이 registry target 상태를 갱신한다.
- Deferred items: F2 grammar 확장은 보류 가능.
- Unresolved important decisions: lease tracker 와 stable registry 의 장기 owner/linearization 모델.
