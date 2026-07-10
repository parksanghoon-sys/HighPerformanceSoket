# Subscription Readiness Seam 설계

- 상태: 사용자 방향 승인, 구현 전 문서 검토 대기
- 관련 결정: D068, D238

## 목표

Dashboard TCP/UDP smoke와 Benchmark TCP/UDP runner가 `BrokerServer._subscriptions`를 reflection으로 읽는
네 개의 cross-module 연결을 제거한다. wire protocol이나 hot-path command 처리는 바꾸지 않고,
in-process host orchestration에 필요한 최소 readiness 계약 하나만 `BrokerServer`에 둔다.

## 확인된 현재 상태

- 네 호출처는 모두 자체 `BrokerServer`를 생성하는 in-process smoke/benchmark 경로다.
- 각 호출처는 SUBSCRIBE 전송 후 `SubscriptionTable.CountSubscribers(topic)`이 목표 값이 될 때까지 10ms 간격으로 polling한다.
- TCP/UDP handler는 SUBSCRIBE command 처리 중 routing table을 동기적으로 갱신한다.
- `Hps.Benchmarks`의 `Hps.Broker` 직접 참조는 이 reflection 기반 `SubscriptionTable` 접근에만 사용된다.
- Server tests의 target identity 검증은 `IsSubscribed(topic, target)`이 필요하므로 별도의 white-box 목적을 가진다.

## 결정

`BrokerServer`에 다음 public method 하나를 추가한다.

```csharp
public Task WaitForSubscriberCountAsync(
    string topic,
    int minimumCount,
    TimeSpan timeout,
    CancellationToken cancellationToken = default)
```

### 의미

- 현재 subscriber count가 `minimumCount` 이상이면 즉시 완료한다.
- 그렇지 않으면 10ms 간격으로 count를 확인한다.
- `minimumCount`가 음수이거나 `timeout`이 0 이하이면 `ArgumentOutOfRangeException`을 던진다.
- topic null/empty 검증은 기존 `SubscriptionTable.CountSubscribers` 계약을 재사용한다.
- 제한 시간 안에 조건을 충족하지 못하면 `TimeoutException`을 던진다.
- cancellation 요청은 `OperationCanceledException`으로 종료한다.
- `Task`를 사용한다. 이 API는 setup/control path이며 100Hz publish hot path에 들어가지 않는다.

이 완료는 in-process 시점의 aggregate count 관측이다. wire SUBSCRIBE ACK, 특정 endpoint identity 확인,
완료 이후 구독 유지, 다음 publish 전달을 보장하지 않는다. 호출자는 isolated host에서 subscriber를 유지한 상태로 사용한다.

## D068과의 경계

D068은 실제 소비자 없이 transport diagnostics를 그대로 pass-through하는 Server convenience API를 보류했다.
이번 seam은 transport diagnostics snapshot이나 범용 server metrics model이 아니다. 이미 네 소비자가 중복 구현한
subscription readiness polling을 한곳으로 모으는 단일 orchestration method다.

다음 항목은 추가하지 않는다.

- `SubscriptionDiagnosticsSnapshot` 같은 새 public type
- subscription changed event/callback
- raw `GetSubscriberCount` getter
- `SubscriptionTable` accessor 또는 constructor injection
- 새 project 또는 `InternalsVisibleTo`

## 대안 검토

### SUBSCRIBE ACK

remote client에도 명시적 완료 신호를 줄 수 있지만 TCP/UDP outbound semantics와 subscriber sample을 모두 바꾼다.
특히 UDP ACK를 신뢰 가능한 완료 신호로 만들려면 재전송, 중복 처리, 순서 정책이 필요하며 현재 범위 밖인
UDP reliability를 연다. 이번 문제는 in-process orchestration이므로 채택하지 않는다.

### PUBLISH/receive preflight probe

새 public API는 피할 수 있지만 probe retry가 여러 payload를 남기거나 benchmark diagnostics와 elapsed time을 오염시킬 수 있다.
TCP/UDP 네 구현에 framing, timeout, drain 규칙도 중복된다. 채택하지 않는다.

### Count getter 또는 event

getter는 네 polling loop를 남기고 event는 subscribe hot path에 callback 수명과 동시성 계약을 추가한다.
현재 요구보다 복잡하므로 채택하지 않는다.

## 구현 범위

- `src/Hps.Server/BrokerServer.cs`: readiness method와 XML doc 추가
- `tests/Hps.Server.Tests/BrokerServerTests.cs`: public shape, timeout/cancellation, 실제 SUBSCRIBE 완료 검증
- Dashboard TCP/UDP smoke: reflection helper와 `System.Reflection` 제거
- Benchmark TCP/UDP runner: reflection helper와 `System.Reflection`, `Hps.Broker` using 제거
- `tests/Hps.Benchmarks/Hps.Benchmarks.csproj`: 불필요해진 `Hps.Broker` project reference 제거

Server tests에서 특정 TCP/UDP target의 구독·재바인딩을 검증하는 `ReadSubscriptionTable`은 유지한다.
단순 count wait helper만 새 API로 이관한다.

## TDD 및 검증 경계

1. reflection shape test로 method 부재 assertion Red를 확인한다.
2. method shape를 추가한 뒤 timeout behavior test를 Red로 만든다.
3. 최소 polling/timeout/cancellation 구현으로 Server tests를 Green으로 만든다.
4. 네 cross-module 호출처와 Server count wait helper를 새 API로 이관한다.
5. `rg`로 네 파일의 `_subscriptions`, `BindingFlags`, `SubscriptionTable` 의존이 0인지 확인한다.
6. Dashboard tests, Benchmark smoke/contract tests, Server tests를 실행한다.
7. solution build 경고 0/오류 0과 solution tests 전체 통과를 확인한다.

## 실패 모드

- subscriber가 등록 직후 해지하면 method 완료 이후 count가 다시 낮아질 수 있다. 이 API는 지속 보장이 아니다.
- shared host에서 `minimumCount` 의미가 모호할 수 있다. 현재 소비자는 isolated host만 사용한다.
- timeout이 너무 짧으면 느린 CI/backend에서 false failure가 난다. 기존 호출처의 5초 기준을 유지한다.
- polling은 control path에서만 사용하며 subscribe/publish handler에 callback이나 추가 lock을 넣지 않는다.

## 범위 밖

- wire ACK/NAK protocol
- UDP reliability, retry, ordering
- endpoint별 readiness 또는 stable identity별 wait
- 범용 server diagnostics/metrics surface
- benchmark 측정 정책 변경
