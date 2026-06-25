# RIO IOCP Notification Completion Wait Design

## 배경

D102 bounded `Task.Yield()` polling 은 RIO의 closed-loop throughput 과 median latency 를 개선했다.
그러나 2026-06-25 scratch benchmark session-03 기준 RIO load p99 는 16689.0 us,
open-loop p99 는 16736.2 us 로 여전히 16ms대 tail 을 보인다.

Microsoft RIO 문서상 polling-only completion queue 는 `RIOCreateCompletionQueue(..., NULL)`로 만들며,
completion notification 을 쓰려면 `RIO_NOTIFICATION_COMPLETION`을 CQ 생성 시 넘기고,
completion 을 한 번 통지받은 뒤 다음 통지를 위해 `RIONotify`를 다시 호출해야 한다.
또한 같은 CQ에 대한 `RIODequeueCompletion` 동시 접근은 외부 동기화가 필요하다.

현재 `RioTransport`는 CQ close/dequeue access violation 을 막기 위해
`RioConnectionResource._completionGate`로 `RIODequeueCompletion`과 `RIOCloseCompletionQueue`를 직렬화한다.
다음 구현은 이 gate 를 유지하면서 timer polling 을 native notification wait 로 교체해야 한다.

참고 문서:
- Microsoft Learn: `RIONotify`
  https://learn.microsoft.com/en-us/windows/win32/api/mswsock/nc-mswsock-lpfn_rionotify
- Microsoft Learn: `RIO_NOTIFICATION_COMPLETION`
  https://learn.microsoft.com/en-us/windows/win32/api/mswsock/ns-mswsock-rio_notification_completion
- Microsoft Learn: `RIOCreateCompletionQueue`
  https://learn.microsoft.com/en-us/windows/win32/api/mswsock/nc-mswsock-lpfn_riocreatecompletionqueue
- Microsoft Learn: `RIODequeueCompletion`
  https://learn.microsoft.com/en-us/windows/win32/api/mswsock/nc-mswsock-lpfn_riodequeuecompletion

## 목표

- RIO receive/send completion wait 의 `Task.Delay(1)` fallback 을 hot path 에서 제거한다.
- RIO p99 tail 이 Windows timer tick 에 묶이는 구조를 없앤다.
- 현재 opt-in RIO backend 의 close/dequeue 안전성을 유지한다.
- SAEA default factory 정책은 바꾸지 않는다.
- RIO unavailable 환경에서는 기존 skip/fallback 테스트 의미를 유지한다.

## 비목표

- 이번 단위에서 RIO를 default backend 로 승격하지 않는다.
- UDP RIO, batching, multi-result drain 최적화, registered buffer 재사용 풀은 포함하지 않는다.
- ThreadPool wait/event 기반 임시 구현을 먼저 넣지 않는다.
- benchmark hard latency gate 를 추가하지 않는다.

## 검토한 접근

### 접근 A — polling budget 추가 확대

`FastCompletionPollYieldCount`를 4096보다 더 크게 늘린다.
구현은 가장 작지만 idle receive 상태에서 scheduler yield 또는 CPU 비용이 커지고,
4096에서도 p99 tail 이 남았으므로 구조적 해법이 아니다.

판정: 채택하지 않는다.

### 접근 B — CQ별 event notification

각 receive/send CQ에 event handle 을 붙이고 `RIONotify` 후 event wait 를 사용한다.
구현 난이도는 IOCP보다 낮지만 connection 당 receive/send event handle 과 wait owner 가 늘어난다.
이 프로젝트의 C10K 방향과 맞지 않고, 이후 shared pump 로 다시 갈아엎을 가능성이 높다.

판정: 보류한다. 디버깅 fallback 으로만 가치가 있다.

### 접근 C — shared IOCP notification pump

`RioTransport`가 하나의 IOCP handle 과 pump task 를 소유한다.
각 `RioConnectionResource`의 receive/send CQ는 `RIO_IOCP_COMPLETION` 설정으로 생성하고,
CQ마다 고유 completion key 와 dedicated `OVERLAPPED` memory 를 가진 signal owner 를 둔다.
`WaitForCompletionAsync(...)`는 먼저 dequeue 를 시도하고, 비어 있으면 해당 CQ의 notification 을 arm 한 뒤
IOCP pump 가 signal 을 깨울 때까지 기다린다.

판정: 채택한다. 구조 변경은 더 크지만 C10K 방향, p99 tail 제거, 후속 batching/shared pump 확장성과 맞다.

## 설계

### 새 책임

`RioNative`
- `RIONotify` delegate 를 실제 method 로 노출한다.
- `RIOCreateCompletionQueue`가 `RIO_NOTIFICATION_COMPLETION` pointer 를 받을 수 있게 overload 를 추가한다.
- `CreateIoCompletionPort`, `GetQueuedCompletionStatusEx`, `PostQueuedCompletionStatus`, `CloseHandle` P/Invoke 는
  RIO project 내부 native 경계에 둔다.

`RioCompletionPort`
- `RioTransport`당 하나 생성한다.
- IOCP handle, pump task, shutdown signal 을 소유한다.
- IOCP completion key 또는 overlapped pointer 로 `RioCompletionSignal`을 찾아 깨운다.
- stop/Dispose 시 pump 를 깨우기 위해 `PostQueuedCompletionStatus`를 호출한다.

`RioCompletionSignal`
- 하나의 RIO CQ에 대응한다. receive CQ와 send CQ가 각각 하나씩 가진다.
- dedicated native `OVERLAPPED` memory 와 completion key 를 소유한다.
- notification armed 상태를 gate 로 보호한다.
- IOCP pump 로부터 signal 을 받으면 waiter 를 깨우고 armed 상태를 해제한다.
- resource dispose 시 waiter 를 깨워 `ObjectDisposedException`으로 수렴하게 한다.

`RioConnectionResource`
- CQ 생성 시 `RioCompletionSignal`의 `RIO_NOTIFICATION_COMPLETION` native pointer 를 넘긴다.
- `DequeueCompletion`과 `NotifyCompletion`을 같은 `_completionGate` 안에서 직렬화한다.
- close 시 signal dispose → socket dispose → CQ close 순서로 정리하되,
  현재처럼 CQ close 는 dequeue/notify 와 같은 gate 로 보호한다.

### Wait flow

1. receive/send pump 가 RIO request 를 post 한다.
2. `WaitForCompletionAsync(resource, cq, signal, connection)`이 `DequeueCompletion`을 먼저 호출한다.
3. completion 이 있으면 즉시 반환한다.
4. completion 이 없고 connection/resource 가 열려 있으면 `resource.ArmNotification(signal)`을 호출한다.
5. `ArmNotification`은 같은 CQ에 이미 outstanding `RIONotify`가 있으면 새로 호출하지 않는다.
   단일 pump/단일 waiter per CQ가 현재 불변식이므로 정상 경로에서는 중복 arm 이 없어야 한다.
6. waiter 는 `signal.WaitAsync()`로 대기한다.
7. IOCP pump 가 RIONotify completion 을 받으면 signal 을 깨운다.
8. waiter 는 루프 처음으로 돌아가 다시 `DequeueCompletion`한다.
9. close/dispose 가 먼저 오면 signal 을 깨우고 `ObjectDisposedException`으로 pump 를 종료한다.

### Thread safety

- 같은 CQ에 대한 dequeue, notify arm, close 는 `RioConnectionResource._completionGate`로 직렬화한다.
- IOCP pump 는 CQ를 직접 dequeue 하지 않는다. pump 는 signal wake 만 담당한다.
  따라서 RIO 문서의 CQ 동시 접근 위험은 기존 wait loop 쪽으로 한정된다.
- `RioCompletionSignal`의 armed flag 는 signal 내부 lock 으로 보호한다.
- close notification 은 D102에서 추가한 `TransportConnection.TryClose()` 전이를 계속 사용한다.

### Error handling

- `RIONotify`가 `ERROR_SUCCESS`를 반환하면 signal armed 상태를 유지한다.
- `RIONotify`가 `WSAEALREADY`를 반환하면 이미 notify 가 outstanding 인 상태로 보고 waiter 는 signal 을 기다린다.
- `RIONotify`가 그 외 오류를 반환하면 `SocketException`으로 pump 를 close notification 경로에 태운다.
- IOCP pump 가 unknown completion key 를 받으면 이미 dispose 된 signal 의 late completion 으로 보고 무시한다.
- IOCP pump 자체가 native error 로 종료되면 모든 등록 signal 을 fault 시켜 waiters 가 멈추지 않게 한다.

### 테스트 전략

1. Red: RIO available 환경에서 기존 D102 latency test 를 유지한다.
   IOCP wait 구현 후에도 median 이 timer-scale 로 회귀하면 실패해야 한다.
2. Red: RIO load/open-loop benchmark artifact 기준 p99 tail 을 수동 관측한다.
   이번 단계에서는 hard gate 로 만들지 않고 `CURRENT_PLAN.md`/`TODOS.md`에 관측값을 기록한다.
3. Green: focused RIO tests 전체를 통과시킨다.
4. Green: close/churn 핵심 RIO tests 를 반복 실행한다.
5. Green: `dotnet build HighPerformanceSocket.slnx --no-restore`,
   `dotnet test HighPerformanceSocket.slnx --no-restore`를 통과시킨다.
6. Observation: RIO `--load`와 `--load-open-loop` report 를 scratch artifact 로 재수집하고,
   p50/p99/actual-rate 를 D102 session-03과 비교한다.

## 구현 분해

1. Native shape task
   - `RIO_NOTIFICATION_COMPLETION`, `OVERLAPPED`, IOCP P/Invoke shape 를 추가한다.
   - `RioNative`가 `RIONotify`와 notification CQ creation 을 노출한다.
   - RIO unavailable 환경에서도 build/test 가 깨지지 않아야 한다.

2. Completion signal task
   - `RioCompletionPort`와 `RioCompletionSignal`을 추가한다.
   - native handle/overlapped lifetime 과 dispose wake 를 단위 테스트 가능한 내부 API로 좁힌다.

3. Resource wiring task
   - `RioConnectionResource`가 receive/send CQ를 notification CQ로 생성한다.
   - `WaitForCompletionAsync`에서 bounded polling fallback 을 제거하고 notify wait 로 전환한다.

4. Hardening/benchmark task
   - close/churn 반복과 RIO load/open-loop artifact 를 수집한다.
   - p99 tail 이 남으면 IOCP pump wake 이후 dequeue timing, request context, per-operation buffer registration 비용을
     별도 후속으로 분리한다.

## Open risks

- `OVERLAPPED` native memory 는 RIONotify completion 이 늦게 도착할 수 있으므로 CQ close 전까지 살아 있어야 한다.
- IOCP pump 가 dispose 중인 signal 을 늦게 깨울 수 있으므로 registry lookup 은 late completion 을 허용해야 한다.
- 현재 RIO send/receive 는 같은 request queue 를 공유한다. send/receive post 자체가 같은 socket RQ에 동시에 들어가므로,
  이미 있는 단일 outstanding receive/send 불변식과 close gate 를 유지해야 한다.
- p99 tail 의 일부가 completion wait 가 아니라 per-operation `RIORegisterBuffer`/`RIODeregisterBuffer` 비용일 수 있다.
  이 경우 IOCP 전환 후에도 tail 이 남을 수 있으며, 그때는 registered buffer reuse 를 별도 D로 승격한다.

## 완료 기준

- polling-only `Task.Delay(1)` wait 가 RIO hot path 에 남지 않는다.
- RIO focused tests, repeated close/wake tests, solution build/test 가 통과한다.
- RIO load/open-loop scratch report 에서 actual-rate, p50, p99 변화가 기록된다.
- p99 tail 이 해결되지 않으면 그 원인 후보와 다음 실험을 `TODOS.md` deferred/current 항목으로 명시한다.
