# io_uring 반복 benchmark artifact 설계

- 날짜: 2026-07-01
- 상태: 구현 기준 설계
- 관련 결정: D146, D147, D148

## 목적

D148로 Linux available runner 에서 `--backend iouring` TCP/UDP raw report, summary, history 를 생성할 수 있음을 확인했다.
다만 run `28486254926`은 protocol 별 `--runs 1` 표본이다. 이 표본은 artifact 경로 검증에는 충분하지만,
TCP p99 warning 2개를 fixed registration 또는 zero-copy 최적화 필요성으로 바로 해석하기에는 부족하다.

따라서 다음 단위는 최적화 구현이 아니라 `iouring-benchmark-artifacts.yml`을 Windows benchmark workflow 와 같은
`--runs 3` 반복 summary 로 맞춰, Linux io_uring 성능 판단의 최소 표본 품질을 올리는 것이다.

## 현재 확인한 사실

- `benchmark-artifacts.yml` Windows workflow 는 `--runs 3`을 사용해 session 안에 load/open-loop raw report 를 3개씩 남긴다.
- `iouring-benchmark-artifacts.yml`은 D147에서 비용과 첫 경로 검증 위험을 낮추기 위해 `--runs 1`로 시작했다.
- D148 artifact 는 TCP/UDP delivery/drop/leak hard gate 를 통과했다.
- D148 artifact 의 TCP p99 warning 은 2개지만, D147 정책상 latency/HWM/warning 은 report data 이며 hard gate 가 아니다.
- 현재 history 는 protocol root 별 session-count 1이다. `--runs 3`은 history session-count 를 늘리지는 않지만,
  같은 session summary 의 source report count 를 2에서 6으로 늘려 summary p50/p99 범위를 더 안정적으로 보여준다.

## 후보 비교

### 후보 A: fixed payload registration cache 를 바로 구현

지금 열지 않는다. `RefCountedBuffer` payload 는 broker fan-out, subscriber pending queue, in-flight send 수명에 걸쳐 공유된다.
io_uring fixed buffer table 에 payload 를 등록하려면 buffer identity, in-flight deregister 금지, eviction,
connection/transport-wide cache 소유권을 먼저 설계해야 한다.

### 후보 B: zero-copy send 를 바로 구현

지금 열지 않는다. `SEND_ZC` 또는 `MSG_ZEROCOPY`는 completion/notification 의미와 payload lifetime 이 더 복잡하다.
4096B x 100Hz 목표에서 zero-copy가 우선 병목인지 확인되지 않았고, 현재 TCP warning 2개는 단일 CI run 표본이다.

### 후보 C: IPv6 direct io_uring UDP

지금 열지 않는다. IPv6는 compatibility 확장이다. 현재 병목 후보를 판단하는 데 필요한 성능 evidence 를 먼저 늘리는 편이 우선이다.

### 후보 D: default backend promotion

지금 열지 않는다. 기본 backend 승격은 contract matrix, fallback policy, 여러 artifact 기준선이 쌓인 뒤 판단해야 한다.
D148은 opt-in Linux benchmark path 검증이지 default 승격 근거가 아니다.

### 후보 E: io_uring benchmark workflow 를 `--runs 3`으로 승격

채택한다. 기존 benchmark CLI와 summary/history schema 를 그대로 재사용하고, workflow static test 하나와 YAML command 두 줄만 바꾸면 된다.
추가 public API, 새 schema, latency hard gate, default selection 변경이 없다.

## 결정

D149: D148 이후 다음 단위는 `iouring-benchmark-artifacts.yml`의 TCP/UDP baseline suite 를 `--runs 3`으로 맞추는 것이다.

- TCP command: `--baseline-suite "$BENCH_TCP_SESSION_DIR" --runs 3 --protocol tcp --backend iouring`
- UDP command: `--baseline-suite "$BENCH_UDP_SESSION_DIR" --runs 3 --protocol udp --backend iouring`
- root summary 에 `Runs per protocol: 3`을 기록한다.
- artifact 구조는 D147/D148과 동일하게 유지한다.
- `workflow_dispatch` 전용, `ubuntu-latest`, runner id `ci-linux-iouring-x64-01`는 유지한다.
- timeout 40분은 유지한다. TCP/UDP 각 3회 load/open-loop 는 현재 run 1회 2분대 결과를 기준으로 충분히 들어간다.

## 테스트 전략

- `BenchmarkArtifactWorkflowTests.IoUringWorkflow_WhenRun_WritesTcpAndUdpArtifactsBeforeFinalFailureGate`가
  TCP/UDP command 에서 `--runs 3`을 기대하도록 먼저 바꾼다.
- 같은 focused test 를 실행해 기존 workflow 의 `--runs 1` 때문에 assertion failure 가 나는 Red를 확인한다.
- workflow command 를 `--runs 3`으로 바꾸고 root summary 에 run 수를 기록한다.
- focused workflow tests 를 통과시킨다.
- solution build/test 와 `git diff --check`로 문서/테스트 변경을 검증한다.

## 범위 밖

- `workflow_dispatch` input 으로 runs 값을 받는 기능.
- 여러 workflow artifact 를 하나의 repository baseline 으로 자동 채택.
- history session-count 를 자동으로 3개로 만드는 multi-session workflow.
- latency hard gate 또는 warning-as-failure.
- fixed buffer registration, zero-copy send, IPv6 direct io_uring UDP.
- `TransportFactory.CreateDefault()` 변경.

## 다음 단계

구현 완료 후 남은 원격 검토 단계만 유지한다.

1. 구현 계획을 작성했다.
2. workflow static test Red를 만들었다.
3. workflow 를 `--runs 3`으로 보정했다.
4. focused workflow tests 를 통과시켰다.
5. 검증과 상태 문서 갱신 후 커밋한다.
6. 사용자 push 이후 원격 artifact 로 source-report-count 6, TCP/UDP hard-passed true, drop/leak 0을 확인했다.

## 원격 검토 결과

- run: `28489104828`
- artifact: `iouring-benchmark-artifacts-2026-07-01-github-28489104828-1`
- root summary: `Runs per protocol: 3`, TCP/UDP baseline/summary/history exit code 0
- TCP summary: source-report-count 6, hard-passed true, warning-count 6,
  load p99 max 4570.8 us, open-loop p99 max 4604.5 us, dropped/payload-error/pool-rented 0,
  TCP HWM max 1
- UDP summary: source-report-count 6, hard-passed true, warning-count 2,
  load p99 max 1506.4 us, open-loop p99 max 1349.3 us, dropped/payload-error/pool-rented 0,
  UDP HWM max 0
- 해석: D149 artifact gate 는 충족했다. p99 warning 은 후속 D150 분석/설계에서 다룬다.
