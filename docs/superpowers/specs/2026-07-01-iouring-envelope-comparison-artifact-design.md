# io_uring Envelope Comparison Artifact 설계

- 날짜: 2026-07-01
- 상태: 구현 기준 설계
- 관련 결정: D125, D127, D147, D149, D150
- 관련 workflow: `.github/workflows/iouring-benchmark-artifacts.yml`

## 배경

D150 반복 benchmark artifact 는 TCP/UDP 모두 delivery, drop, payload error, pool leak hard gate 를 통과했다.
남은 신호는 p99 latency soft warning 이다.
이 warning 은 `BaselineSummaryGenerator`의 D070 전역 threshold 에서 나온 값이며,
Linux `io_uring` runner/profile 에 맞는 reference envelope 를 읽어 판단한 결과가 아니다.

따라서 D150 warning 을 fixed registration, zero-copy send, default backend promotion 의 직접 근거로 쓰면 안 된다.
이미 D125에서 runner/profile scoped 판단은 기존 `warning-count`가 아니라 별도 envelope comparison artifact 로 분리하기로 했다.
Windows benchmark workflow 는 D127 기준으로 이 artifact 를 생성하지만,
Linux `io_uring` benchmark workflow 는 아직 protocol별 envelope comparison step 이 없다.

## 목표

`iouring-benchmark-artifacts.yml`이 TCP/UDP protocol별 summary/history 생성 뒤에
기존 `--compare-baseline-envelope` command 를 실행할 수 있게 한다.

목표는 p99 warning 을 즉시 failure 로 승격하는 것이 아니라, 다음 artifact 부터 아래 질문을 기계적으로 답하게 만드는 것이다.

1. 같은 Linux `io_uring` runner/profile/protocol reference 가 존재하는가.
2. 존재한다면 이번 candidate summary 가 reference envelope 와 비교 가능한가.
3. 비교 가능하다면 p99/HWM/actual-rate/drop/leak metric 이 envelope signal 을 내는가.

## 선택지

### 선택지 A: fixed registration 또는 zero-copy 를 바로 구현

채택하지 않는다.
D150 artifact 에서 drop, payload error, pool leak, TCP HWM, UDP HWM 은 안정적이다.
현재 p99 warning 만으로 payload fixed registration cache 또는 zero-copy send 의 소유권 비용을 열기에는 근거가 좁다.

### 선택지 B: `BaselineSummaryGenerator` 전역 threshold 를 io_uring 값으로 조정

채택하지 않는다.
전역 threshold 는 SAEA/RIO/UDP/CI/local runner 에 모두 적용된다.
Linux `io_uring` 한 runner 의 관측값을 전역 상수에 반영하면 false signal 또는 false negative 를 만든다.

### 선택지 C: io_uring workflow 에 protocol별 envelope comparison step 을 추가

채택한다.
기존 D125 command 와 D127 workflow 정책을 재사용하므로 새 schema 나 새 public API가 필요 없다.
reference history 가 없으면 skip 하고, 있으면 `envelope.json`/`envelope.md`를 artifact 에 포함한다.

## 결정

D151: D150 p99 warning 은 최적화 구현이 아니라 io_uring protocol별 envelope comparison artifact 연결로 먼저 해석한다.

- TCP reference history path:
  `docs/benchmarks/baselines/runners/${HPS_BENCHMARK_RUNNER_ID}/tcp/history.json`
- UDP reference history path:
  `docs/benchmarks/baselines/runners/${HPS_BENCHMARK_RUNNER_ID}/udp/history.json`
- TCP output:
  `$BENCH_TCP_ROOT/envelope.json`, `$BENCH_TCP_ROOT/envelope.md`
- UDP output:
  `$BENCH_UDP_ROOT/envelope.json`, `$BENCH_UDP_ROOT/envelope.md`
- reference history 가 없으면 해당 protocol envelope step 은 skip 하고 exit code 0을 기록한다.
- reference history 가 있으면 기존 `--compare-baseline-envelope` command 를 실행한다.
- envelope mismatch 또는 signal 은 report-only 다.
- command usage/schema/write failure 는 artifact generation failure 로 본다.

## artifact 구조

현재 io_uring workflow 는 protocol root 를 분리한다.

```text
artifacts/benchmarks/runners/ci-linux-iouring-x64-01/
  tcp/
    <yyyy-mm-dd>/session-01/
    history.json
    history.md
    envelope.json
    envelope.md
  udp/
    <yyyy-mm-dd>/session-01/
    history.json
    history.md
    envelope.json
    envelope.md
```

repository baseline 으로 수동 채택할 때도 같은 protocol split 을 유지해야 한다.
TCP와 UDP는 `benchmark-profile`, `scenario`, transport path 가 다르므로 같은 runner root history 에 섞지 않는다.

## 실패 정책

workflow failure 로 본다.

- baseline suite command failure
- summary command failure
- history command failure
- reference history 가 있는데 envelope command 가 usage/schema/write error 로 실패

workflow failure 로 보지 않는다.

- reference history 없음으로 envelope step skip
- `envelope-compatible=false`
- `envelope-signal-count > 0`
- 기존 `warning-count > 0`

## 범위 밖

- latency hard gate 또는 warning-as-failure
- fixed payload registration cache
- zero-copy send
- `TransportFactory.CreateDefault()` promotion
- io_uring benchmark artifact 자동 repository baseline 채택
- reference baseline 파일 자체 추가

## 검증

- workflow static test 가 TCP/UDP envelope step 순서와 command path 를 고정한다.
- Red: io_uring workflow 에 envelope step 이 없어서 focused workflow test 가 실패해야 한다.
- Green: workflow step 추가 후 focused `BenchmarkArtifactWorkflowTests`가 통과해야 한다.
- 전체 검증은 `dotnet build`, `dotnet test`, `git diff --check`로 마무리한다.
