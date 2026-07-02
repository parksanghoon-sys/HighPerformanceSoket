# TODOS.md

## Archive

이 파일은 현재 실행 가능한 항목과 소수의 deferred backlog 만 유지한다. 긴 완료 이력은 archive 를 본다.

- 완료 이력 원문: `docs/agent-state/backlog/completed-history-2026-06-18.md`
- 전체 pre-compaction snapshot: `docs/agent-state/snapshots/2026-06-18-pre-compaction/`

## Current TODOs

- [ ] D166 기준으로 D165 raw report 를 protocol별 두 번째 date root reference 로 수동 채택한다.
  - 입력: `docs/superpowers/specs/2026-07-02-iouring-post-d165-reference-date-expansion-design.md`.
  - 할 일: run `28566385562` TCP raw report 를 `tcp/2026-07-02/session-01`,
    UDP raw report 를 `udp/2026-07-02/session-01`로 복사한다.
  - 확인할 것: TCP protocol history session-count 4, UDP protocol history session-count 7,
    hard-passed true, comparison-compatible true, 최신 session envelope smoke signal 0.
  - 제외: 자동 baseline 채택, latency hard gate, fixed registration, zero-copy 구현.

## Deferred Backlog

- [ ] `P2_LATER` RIO full IPv6 지원은 default promotion scope 가 다시 열릴 때 별도 설계로 판단한다.
  - 무엇이 남았는지: RIO backend 는 D122 기준 TCP/UDP 모두 현재 IPv4 `IPEndPoint` 전용이다.
    sample broker host 의 `auto` mode 는 non-IPv4 listen endpoint 에서 SAEA fallback 을 제공하므로,
    즉시 운영 가능한 fallback 정책은 구현되어 있다.
  - 왜 defer 되었는지: D118의 4096B x 100Hz RIO UDP scratch evidence 는 IPv4 loopback 기준이고,
    D119/D122에 따라 base default promotion 은 아직 열려 있지 않다.
  - objective: RIO를 IPv6 endpoint 에서도 직접 backend 로 사용하거나 default promotion 대상으로 다시 평가하려면
    TCP/UDP IPv6 socket, sockaddr, tests, benchmark evidence 를 완성한다.
  - relevant context: D110, D118, D119, D121, D122,
    `docs/superpowers/specs/2026-06-26-rio-udp-ipv6-support-gate-design.md`,
    `docs/superpowers/specs/2026-06-29-rio-address-family-aware-selection-policy-design.md`.
  - 관련 파일/범위: `src/Hps.Transport.Rio/RioNative.cs`, `src/Hps.Transport.Rio/RioTransport.cs`,
    `src/Hps.Transport.Rio/RioUdpEndpoint.cs`, RIO TCP/UDP contract tests, benchmark explicit RIO path.
  - 현재 상태 또는 이미 시도한 접근: UDP IPv6 local/remote guard 와 TCP IPv6 listen/connect guard 가 구현됐다.
    sample broker host 는 address-family-aware selector 로 IPv6 auto fallback/explicit rio failure 를 제공한다.
  - known blockers 또는 open questions: `SOCKADDR_IN6` encode/decode, scope id, dual-mode socket 정책,
    IPv6 benchmark artifact 채택 여부, default promotion 의 IPv6 compatibility 요구 수준.
  - 가장 자연스러운 next step: default promotion scope 가 열리면 full IPv6 implementation plan 과 composite/fallback 유지안을 비교한다.

- [ ] `P3_NICE` 실제 host/metrics surface 가 생기면 server-level diagnostics model 을 설계한다.
  - 무엇이 남았는지: D068로 `BrokerServer` 단순 pass-through diagnostics API 는 v1에 추가하지 않기로 했다.
  - 왜 defer 되었는지: 현재 서버는 단일 injected `ITransport` 를 감싼 얇은 host 이며, diagnostics 소비자는 테스트/benchmark 중심이다.
  - objective: 실제 host/운영 API가 구체화된 뒤 server-level diagnostics model 이 필요한지 결정한다.
  - relevant context: D041, D042, D056, D062, D066, D068, `docs/superpowers/specs/2026-06-18-server-diagnostics-surface-design.md`.
  - 관련 파일/범위: `src/Hps.Server/`, `src/Hps.Transport/`, host/sample 코드, 관련 tests.
  - next step: metrics/exporter 또는 server-only consumer 요구가 나오면 별도 설계로 승격한다.

## Completed

최근 완료 항목만 유지한다. 전체 완료 이력은 `docs/agent-state/backlog/completed-history-2026-06-18.md`를 본다.

- [x] D157 결과를 바탕으로 UDP open-loop p50-median 반복 signal triage 를 설계했다.
  - 범위: `docs/superpowers/specs/2026-07-01-iouring-udp-open-loop-p50-triage-design.md`,
    D158 상태/결정 문서.
  - 결정: 반복 signal 은 transport 최적화 필요성보다 1-session provisional reference 가 너무 얇은 문제로 보고,
    D155~D157 UDP candidate raw reports 를 session-02..04로 수동 채택해 reference 를 안정화한다.
  - 제외: TCP protocol root 확장, fixed registration, zero-copy, latency hard gate.

- [x] D158 기준으로 D155~D157 UDP candidate raw reports 를 provisional reference sessions 로 수동 채택했다.
  - 범위: `docs/benchmarks/baselines/runners/ci-linux-iouring-x64-01/udp/2026-07-01/session-02..04`,
    UDP `history.json`/`history.md`, `docs/benchmarks/baselines/index.md`, 상태 문서.
  - 결과: UDP protocol root history 는 session-count 4, hard-passed true, warning-count 8,
    comparison-compatible true 상태다.
  - 검증: updated reference envelope smoke 는 `envelope-compatible=true`, `envelope-signal-count=0`으로 통과했다.
  - 의미: 반복된 open-loop p50 signal 은 얇은 provisional reference 문제로 해석하고,
    fixed registration, zero-copy, latency hard gate 는 계속 보류한다.

- [x] D158 안정화 이후 io_uring 후속 후보를 재평가했다.
  - 범위: `docs/superpowers/specs/2026-07-01-iouring-post-d158-next-scope-design.md`, D159 상태/결정 문서.
  - 결정: 다음 단위는 fixed registration/zero-copy/default promotion 이 아니라
    updated reference 를 반영한 원격 artifact gate 다.
  - 근거: UDP p50 signal 은 D158 smoke 에서 닫혔고, drop/payload-error/pool-rented/hard gate failure 가 없다.
  - 다음: push 이후 원격 workflow run 이 생기면 artifact 를 검토한다.

- [x] D159 기준 updated reference 가 반영된 원격 `iouring-benchmark-artifacts.yml` artifact 를 검토했다.
  - 범위: GitHub Actions run `28495804466`,
    artifact `iouring-benchmark-artifacts-2026-07-01-github-28495804466-1`, D160 상태/결정 문서.
  - 결과: workflow success, TCP/UDP baseline/summary/history/envelope exit code 0.
  - evidence: TCP/UDP raw report count 는 각각 6이고, TCP/UDP envelope 는 모두 compatible true, signal-count 0이다.
  - 의미: D158 updated reference 로 UDP open-loop p50 반복 signal 이 원격 artifact 에서도 해소됐다.

- [x] benchmark Markdown HWM label 을 protocol-neutral 하게 정리했다.
  - 범위: `BaselineSummaryMarkdownWriter`, `BaselineHistoryMarkdownWriter`, 관련 tests,
    generated baseline Markdown artifact, `docs/benchmarks/baselines/index.md`, D161 상태/결정 문서.
  - 결정: Markdown 헤더는 `send queue HWM max`로 바꾸고, JSON schema 의 `tcp-hwm-*` field 는 호환성을 위해 유지한다.
  - 검증: focused Red 실패 2개 확인 후 Green, `Hps.Benchmarks.Tests` 114개 통과, `git diff --check` 통과.

- [x] D161 Markdown label 변경이 반영된 원격 `iouring-benchmark-artifacts.yml` artifact 를 검토했다.
  - 범위: GitHub Actions run `28497147332`,
    artifact `iouring-benchmark-artifacts-2026-07-01-github-28497147332-1`, D162 상태/결정 문서.
  - 결과: workflow success, TCP/UDP baseline/summary/history/envelope exit code 0.
  - evidence: TCP/UDP raw report count 는 각각 6이고, TCP/UDP envelope 는 모두 compatible true, signal-count 0이다.
  - evidence: TCP/UDP summary/history Markdown 에 `send queue HWM max` label 이 반영됐다.

- [x] D162 이후 io_uring 후속 후보를 재평가했다.
  - 범위: `docs/superpowers/specs/2026-07-01-iouring-post-d162-reference-expansion-design.md`, D163 상태/결정 문서.
  - 결정: 다음 단위는 fixed registration/zero-copy/latency gate 가 아니라
    D160/D162 raw report 를 protocol별 provisional reference 로 수동 채택하는 것이다.
  - 근거: D160/D162는 failure artifact 가 아니라 envelope signal 0 passing artifact 이므로 reference 표본 확장이 더 적합하다.

- [x] D163 기준으로 D160/D162 raw report 를 protocol별 provisional reference sessions 로 수동 채택했다.
  - 범위: `docs/benchmarks/baselines/runners/ci-linux-iouring-x64-01/tcp/2026-07-01/session-02..03`,
    `docs/benchmarks/baselines/runners/ci-linux-iouring-x64-01/udp/2026-07-01/session-05..06`,
    protocol별 history, `docs/benchmarks/baselines/index.md`, D164 상태/결정 문서.
  - 결과: TCP protocol root history 는 session-count 3, hard-passed true, warning-count 18,
    comparison-compatible true 상태다.
  - 결과: UDP protocol root history 는 session-count 6, hard-passed true, warning-count 12,
    comparison-compatible true 상태다.
  - 검증: 최신 session 기준 envelope smoke 는 TCP/UDP 모두 `envelope-compatible=true`,
    `envelope-signal-count=0`으로 통과했다.
  - 다음: 사용자 push 이후 원격 artifact gate 로 확장된 reference 가 workflow 에서도 정상 사용되는지 확인한다.

- [x] D164 reference 확장 이후 원격 `iouring-benchmark-artifacts.yml` artifact gate 를 검토했다.
  - 범위: GitHub Actions run `28566385562`,
    artifact `iouring-benchmark-artifacts-2026-07-02-github-28566385562-1`, D165 상태/결정 문서.
  - 결과: workflow success, TCP/UDP baseline/summary/history/envelope exit code 0.
  - evidence: TCP/UDP raw report count 는 각각 6이고, hard-passed true, drop/payload-error/pool-rented 0이다.
  - evidence: TCP envelope 는 reference-summary-count 3, signal-count 0이다.
  - evidence: UDP envelope 는 reference-summary-count 6, signal-count 0이다.
  - 의미: D164 확장 reference history 가 원격 workflow artifact 에서 실제 envelope reference 로 사용됐다.

- [x] D165 이후 io_uring 후속 후보를 재평가했다.
  - 범위: `docs/superpowers/specs/2026-07-02-iouring-post-d165-reference-date-expansion-design.md`,
    D166 상태/결정 문서.
  - 결정: 다음 단위는 fixed registration/zero-copy/default promotion/latency gate 가 아니라
    D165 passing artifact 를 protocol별 두 번째 date root reference 로 수동 채택하는 것이다.
  - 근거: D165는 correctness/reliability failure 가 아니라 expanded reference 기준 signal 0 evidence 이므로
    최적화 구현보다 multi-date reference 안정화가 현재 evidence 에 맞다.

- [x] D156 기준으로 `iouring-benchmark-artifacts.yml` reference-present candidate 2개를 추가 수집하고 UDP signal 반복성을 검토했다.
  - 범위: GitHub Actions runs `28494135787`, `28494404015`, D157 상태/결정 문서.
  - 결과: 두 run 모두 workflow success, TCP/UDP baseline/summary/history/envelope exit 0이다.
  - evidence: run `28494135787`은 TCP envelope signal 0, UDP envelope signal 2를 기록했다.
    UDP signals 는 load `p99-growth-ratio-max`, open-loop `p50-median-us`다.
  - evidence: run `28494404015`는 TCP envelope signal 0, UDP envelope signal 1을 기록했다.
    UDP signal 은 open-loop `p50-median-us`다.
  - 결론: D155 포함 3개 candidate 모두 UDP open-loop `p50-median-us` signal 을 반복했다.
  - 다음: UDP latency triage 설계를 열고, 바로 fixed registration/zero-copy 구현으로 가지 않는다.

- [x] D155 UDP envelope signal 이후 provisional reference 확장/triage 정책을 설계했다.
  - 범위: `docs/superpowers/specs/2026-07-01-iouring-udp-envelope-signal-triage-policy-design.md`,
    D156 상태/결정 문서.
  - 결정: D155 UDP signal 은 즉시 최적화 구현으로 연결하지 않고,
    추가 reference-present artifact 2개를 더 수집해 반복성 여부를 판단한다.
  - 근거: D154 reference 는 1-session provisional baseline 이므로 measurement variance 와 구조 문제를 구분하기 어렵다.
  - 다음: 원격 workflow 를 2회 더 실행해 D155 포함 3개 candidate 의 UDP signal 을 표로 정리한다.

- [x] 사용자 push 이후 `iouring-benchmark-artifacts.yml` reference-present envelope artifact 경로를 원격 검토했다.
  - 범위: GitHub Actions run `28493590950`,
    artifact `iouring-benchmark-artifacts-2026-07-01-github-28493590950-1`.
  - 결과: workflow conclusion success, job `io_uring benchmark artifacts (linux)` success.
  - evidence: root `summary.md`는 TCP/UDP baseline, summary, history, envelope exit code 를 모두 0으로 기록했다.
  - evidence: TCP/UDP 각각 raw report 6개, protocol 별 `summary.json`/`summary.md`,
    `history.json`/`history.md`, `envelope.json`/`envelope.md`가 존재한다.
  - evidence: TCP envelope 는 `envelope-compatible=true`, `envelope-signal-count=0`이다.
  - evidence: UDP envelope 는 `envelope-compatible=false`, `envelope-signal-count=2`다.
    signal 은 load `p99-max-us` upper bound 초과와 open-loop `p50-median-us` upper bound 초과다.
  - 의미: reference-present envelope artifact path 는 검증됐다(D155).
    UDP signal 은 D153 provisional reference 상태의 report-only triage 대상으로 남긴다.

- [x] run `28492234252` artifact 를 `io_uring` protocol별 provisional repository reference baseline 으로 수동 채택했다.
  - 범위: `docs/benchmarks/baselines/runners/ci-linux-iouring-x64-01/tcp/**`,
    `docs/benchmarks/baselines/runners/ci-linux-iouring-x64-01/udp/**`,
    `docs/benchmarks/baselines/index.md`, D154 상태/결정 문서.
  - 결과: TCP/UDP raw report 6개씩을 protocol별 `2026-07-01/session-01`에 복사하고,
    summary/history 를 repository 경로 기준으로 재생성했다.
  - 검증: TCP/UDP summary source-report-count 6, hard-passed true,
    protocol root history session-count 1, hard-passed true.
  - 검증: protocol별 envelope command smoke 는 TCP/UDP 모두 `envelope-compatible=true`,
    `envelope-signal-count=0`으로 통과했다.
  - 비고: warning-count 는 TCP 6, UDP 3이며 D153 기준 provisional reference signal 로 기록하고
    latency hard gate 또는 warning-as-failure 로 승격하지 않는다.

- [x] D152 이후 `io_uring` protocol별 repository reference baseline 수동 채택 정책을 설계했다.
  - 범위: `docs/superpowers/specs/2026-07-01-iouring-protocol-reference-baseline-adoption-policy-design.md`,
    D153 상태/결정 문서.
  - 결정: D152 artifact 는 TCP/UDP 모두 hard gate 와 comparison compatibility 를 통과했으므로
    첫 protocol별 provisional repository reference baseline 으로 수동 채택할 수 있다.
  - 차이: D095는 warning-count 0을 요구하지만, 초기 `io_uring` reference 의 warning 은 전역 soft threshold signal 이므로
    채택 차단 조건으로 보지 않고 provisional 표시로 남긴다.
  - 다음: artifact raw report 를 protocol별 repository baseline 구조로 복사하고 summary/history/index 를 재생성한다.

- [x] 사용자 push 이후 `iouring-benchmark-artifacts.yml` D151 envelope artifact 경로를 원격 검토했다.
  - 범위: GitHub Actions run `28492234252`,
    artifact `iouring-benchmark-artifacts-2026-07-01-github-28492234252-1`.
  - 결과: workflow conclusion success, job `io_uring benchmark artifacts (linux)` success.
  - evidence: root `summary.md`는 TCP/UDP baseline, summary, history, envelope exit code 를 모두 0으로 기록했다.
  - evidence: TCP/UDP 각각 raw report 6개, protocol 별 `summary.json`/`summary.md`,
    `history.json`/`history.md`가 존재한다.
  - evidence: repository reference history 가 아직 없어 `tcp/envelope.json`, `udp/envelope.json`은 생성되지 않았고,
    skip 경로가 정상 exit 0으로 수렴했다.
  - evidence: TCP summary 는 source-report-count 6, hard-passed true, warning-count 6,
    load p99 max 4298.8 us, open-loop p99 max 5588.6 us, dropped/payload-error/pool-rented 0,
    TCP HWM max 1이다.
  - evidence: UDP summary 는 source-report-count 6, hard-passed true, warning-count 3,
    load p99 max 1623.8 us, open-loop p99 max 1322.0 us, dropped/payload-error/pool-rented 0,
    UDP HWM max 0이다.
  - 의미: D151 protocol별 envelope step 의 원격 artifact/skip gate 를 충족했다(D152).
  - 다음: envelope comparison 이 실제 signal 을 낼 수 있도록 protocol별 repository reference baseline 수동 채택 정책을 설계한다.

- [x] D150 p99 warning 을 분석하고 io_uring protocol별 envelope comparison artifact 단위를 구현했다.
  - 범위: `.github/workflows/iouring-benchmark-artifacts.yml`,
    `tests/Hps.Benchmarks.Tests/BenchmarkArtifactWorkflowTests.cs`,
    `docs/superpowers/specs/2026-07-01-iouring-envelope-comparison-artifact-design.md`,
    `docs/superpowers/plans/2026-07-01-iouring-envelope-comparison-artifact.md`, 상태 문서.
  - 결정: D150 warning 은 delivery/drop/leak 문제가 아니라 D070 전역 soft threshold 에서 나온 p99 signal 이므로,
    fixed registration/zero-copy/default promotion 전에 기존 D125 envelope comparison artifact 경로로 해석한다.
  - Red: io_uring workflow 에 envelope step 이 없어 focused workflow static test 1개가 실패함을 확인했다.
  - Green: TCP/UDP history 뒤에 protocol별 `--compare-baseline-envelope` step 을 추가하고,
    reference history 가 없으면 skip + exit 0으로 기록하도록 했다.
  - 검증: `BenchmarkArtifactWorkflowTests` focused test 6개 통과,
    solution build 경고 0/오류 0, solution tests 445개 통과, `git diff --check` whitespace 오류 없음(CRLF 경고만 있음).
  - 다음: 커밋 후 사용자 push 이후 원격 workflow artifact 로 envelope skip/generation 경로를 확인한다.

- [x] D148 이후 io_uring 후속 후보를 재평가하고 반복 benchmark artifact 단위를 구현했다.
  - 범위: `.github/workflows/iouring-benchmark-artifacts.yml`,
    `tests/Hps.Benchmarks.Tests/BenchmarkArtifactWorkflowTests.cs`,
    D149 설계/계획/상태 문서.
  - 결정: fixed registration, zero-copy send, IPv6 direct io_uring UDP, default backend promotion 은
    단일 artifact 성공만으로 바로 열지 않고, 먼저 TCP/UDP `--runs 3` 반복 summary 를 수집한다.
  - Red: workflow static test 를 `--runs 3` 기대값으로 바꾼 뒤 기존 workflow 의 `--runs 1` 때문에
    focused test assertion failure 1개를 확인했다.
  - Green: workflow TCP/UDP baseline suite command 를 `--runs 3`으로 보정하고,
    root summary 에 `Runs per protocol: 3`을 추가한 뒤 focused workflow tests 5개 통과를 확인했다.
  - 다음: 사용자 push 이후 원격 workflow artifact 로 source-report-count 6과 hard gate 통과 여부를 검토한다.

- [x] 사용자 push 이후 `iouring-benchmark-artifacts.yml` `--runs 3` artifact 를 검토했다.
  - 범위: GitHub Actions run `28489104828`,
    artifact `iouring-benchmark-artifacts-2026-07-01-github-28489104828-1`.
  - 결과: workflow conclusion success, job `io_uring benchmark artifacts (linux)` success.
  - evidence: root `summary.md`는 `Runs per protocol: 3`과 TCP/UDP baseline/summary/history exit code 0을 기록했다.
  - evidence: TCP/UDP 각각 raw report 6개, protocol 별 `summary.json`/`summary.md`,
    `history.json`/`history.md`가 존재한다.
  - evidence: TCP summary 는 source-report-count 6, hard-passed true, warning-count 6,
    load p99 max 4570.8 us, open-loop p99 max 4604.5 us, dropped/payload-error/pool-rented 0,
    TCP HWM max 1이다.
  - evidence: UDP summary 는 source-report-count 6, hard-passed true, warning-count 2,
    load p99 max 1506.4 us, open-loop p99 max 1349.3 us, dropped/payload-error/pool-rented 0,
    UDP HWM max 0이다.
  - 의미: D149 반복 benchmark artifact gate 를 충족했다(D150).
  - 다음: p99 warning 을 최적화 구현 근거로 볼지, runner/profile scoped threshold 정책 문제로 볼지 설계로 판단한다.

- [x] 사용자 push 이후 `iouring-benchmark-artifacts.yml` 원격 artifact 를 검토했다.
  - 범위: GitHub Actions run `28486254926`,
    artifact `iouring-benchmark-artifacts-2026-07-01-github-28486254926-1`.
  - 결과: workflow conclusion success, job `io_uring benchmark artifacts (linux)` success.
  - evidence: root `summary.md` 기준 TCP/UDP baseline, summary, history exit code 가 모두 0이다.
  - evidence: artifact 는 `tcp/2026-07-01/session-01`, `udp/2026-07-01/session-01`,
    protocol 별 `history.json`/`history.md`, root `summary.md`, `dotnet-info.txt`를 포함한다.
  - evidence: TCP summary 는 source-report-count 2, hard-passed true, warning-count 2,
    dropped-total 0, payload-error-total 0, pool-rented-max 0이다.
  - evidence: UDP summary 는 source-report-count 2, hard-passed true, warning-count 0,
    dropped-total 0, payload-error-total 0, pool-rented-max 0이다.
  - 의미: D147의 원격 Linux `io_uring` TCP/UDP benchmark artifact gate 를 충족했다(D148).
  - 다음: fixed registration, zero-copy send, IPv6 direct io_uring UDP, default promotion 후보를 다시 평가한다.

- [x] Linux io_uring benchmark artifact workflow 를 추가했다.
  - 범위: `.github/workflows/iouring-benchmark-artifacts.yml`,
    `tests/Hps.Benchmarks.Tests/BenchmarkArtifactWorkflowTests.cs`, D147 설계/계획/상태 문서.
  - Red: workflow static test 가 `iouring-benchmark-artifacts.yml` missing failure 로 실패하는 것을 확인했다.
  - Green: workflow 추가 후 `BenchmarkArtifactWorkflowTests` focused test 4개 통과를 확인했다.
  - 구현: `workflow_dispatch` 전용 `ubuntu-latest` workflow 가 TCP/UDP 각각
    `--baseline-suite ... --runs 1 --protocol <tcp|udp> --backend iouring`을 실행하고,
    summary/history/root summary/dotnet info 를 artifact 로 업로드한다.
  - 비고: 기존 Windows benchmark workflow, default backend selection, latency hard gate,
    fixed registration, zero-copy send 는 열지 않았다.
  - 다음: 사용자 push 이후 원격 workflow 를 수동 실행하고 artifact 를 검토한다.

- [x] io_uring benchmark artifact workflow 원격 실패 원인을 확인하고 history root 구조를 보정했다.
  - 범위: GitHub Actions run `28485295725`, `.github/workflows/iouring-benchmark-artifacts.yml`,
    workflow static test, D147 상태/설계 문서.
  - 결과: TCP/UDP baseline suite 와 summary command 는 exit 0이었고 raw report/summary artifact 는 생성됐다.
  - 원인: history command 입력이 `date/protocol/session` 구조의 protocol directory 였기 때문에
    `BaselineHistoryReader`가 요구하는 "입력 root 바로 아래 날짜 directory" 규칙을 만족하지 못했다.
  - 보정: artifact 구조를 `runner/tcp/<yyyy-mm-dd>/session-01`,
    `runner/udp/<yyyy-mm-dd>/session-01`로 바꾸고 protocol root 를 history input 으로 유지한다.
  - 다음: 보정 커밋 push 이후 원격 workflow 를 다시 실행해 history artifact 생성까지 확인한다.

- [x] io_uring benchmark backend selector 구현 계획을 완료했다.
  - 범위: `tests/Hps.Benchmarks` CLI/backend selector, TCP/UDP scenario identity, tests.
  - Red: parser/identity focused test 에서 `iouring` invalid backend 및 enum 누락으로 3개 실패 확인.
  - Green: parser/identity focused test 36개 통과.
  - Red: scenario/help focused test 에서 TCP/UDP scenario key 와 help text 3개 실패 확인.
  - Green: scenario/help focused test 7개 통과.
  - 구현: `--backend iouring`, `IoUringTransport` report identity, TCP/UDP io_uring scenario key,
    Linux/capability gated transport factory 를 추가했다.
  - 검증: `dotnet build HighPerformanceSocket.slnx --no-restore -v minimal` 경고 0/오류 0,
    `dotnet test HighPerformanceSocket.slnx --no-build --no-restore -v minimal` 전체 통과.
  - 다음: 사용자 push 이후 Linux available runner 에서 io_uring benchmark artifact 를 수집해 검토한다.

- [x] D144 이후 io_uring 후속 후보를 재평가하고 다음 설계 단위를 확정했다.
  - 범위: fixed payload registration cache, receive fixed buffer registration, zero-copy send,
    IPv6 direct io_uring UDP, default backend promotion, benchmark backend selector.
  - 결과: D145로 benchmark CLI `--backend iouring` selector 를 다음 구현 단위로 선택했다.
  - 산출물: `docs/superpowers/specs/2026-06-30-iouring-benchmark-backend-selector-design.md`,
    `docs/superpowers/plans/2026-06-30-iouring-benchmark-backend-selector.md`.
  - 근거: D144는 contract gate 이지 성능 artifact 가 아니므로, 최적화나 default promotion 전에
    TCP/UDP loopback raw benchmark 를 io_uring backend 로 남길 수 있어야 한다.
  - 다음: 구현 계획 Task 1 parser/identity contract 를 TDD로 시작한다.

- [x] 사용자 push 이후 `iouring-linux-contract` artifact 로 io_uring UDP bounded receive window 를 검토했다.
  - 범위: GitHub Actions run `28424009519`, artifact `iouring-linux-contract-2026-06-30-github-28424009519-1`.
  - 결과: workflow conclusion success, job `io_uring contract (linux)` success, test exit code 0.
  - evidence: TRX counters total 55 / executed 55 / passed 55 / failed 0 / error 0 / timeout 0.
  - evidence: `IoUringCapabilityEvidenceTests` stdout 은 `io_uring capability status: Available`,
    OS `Ubuntu 24.04.4 LTS`, architecture `X64`, process architecture `X64`를 기록했다.
  - evidence: UDP receive/echo/endpoint diagnostics tests 와
    `UdpReceive_WhenHandlerIsBlocked_PreservesWindowedDatagrams`가 Passed 였다.
  - 의미: D143 bounded receive slot window 의 Linux native artifact gate 를 충족했다.
  - 다음: D144 이후 io_uring 후속 후보를 재평가하고 다음 설계 단위를 확정한다.

- [x] D142 이후 후속 후보를 재평가하고 io_uring UDP receive window 를 구현했다.
  - 범위: `docs/superpowers/specs/2026-06-30-iouring-udp-receive-window-design.md`,
    `docs/superpowers/plans/2026-06-30-iouring-udp-receive-window.md`,
    `src/Hps.Transport.IoUring/IoUringUdpEndpoint.cs`,
    `src/Hps.Transport.IoUring/IoUringTransport.cs`,
    `tests/Hps.Transport.IoUring.Tests/IoUringUdpEndpointShapeTests.cs`,
    `tests/Hps.Transport.IoUring.Tests/IoUringTransportUdpTests.cs`.
  - 결과: D143으로 fixed registration/zero-copy/default promotion 보다 receive-side bounded slot window 를 먼저 열기로 결정했다.
  - 구현: `ReceiveWindowSize = 4`, receive slot 별 context/message buffer/in-flight datagram ownership,
    handler dispatch 전 slot repost 순서를 추가했다.
  - 검증: focused shape Red/Green, `IoUringTransportUdpTests`, `IoUringUdpEndpointShapeTests`,
    `Hps.Transport.IoUring.Tests` 55개 통과.
  - 다음: 사용자 push 이후 원격 `iouring-linux-contract` artifact 로 Linux native bounded window path 를 검토한다.

- [x] 원격 `iouring-linux-contract` workflow 실행 결과로 io_uring UDP pump artifact 를 검토했다.
  - 범위: GitHub Actions run `28421177310`, artifact `iouring-linux-contract-2026-06-30-github-28421177310-1`.
  - 결과: workflow conclusion success, job `io_uring contract (linux)` success, test exit code 0.
  - evidence: TRX counters total 52 / executed 52 / passed 52 / failed 0 / error 0 / timeout 0.
  - evidence: `IoUringCapabilityEvidenceTests` stdout 은 `io_uring capability status: Available`,
    OS `Ubuntu 24.04.4 LTS`, architecture `X64`, process architecture `X64`를 기록했다.
  - evidence: UDP 핵심 경로인 `UdpReceive_WhenIoUringAvailable_DeliversOwnedRefCountedBuffer`와
    `UdpEcho_WhenIoUringAvailable_QueuesResponseAndClientReceivesPayload`가 Passed 였다.
  - 의미: D140 UDP v1의 Linux native `recvmsg`/`sendmsg` artifact gate 를 충족했다.
  - 다음: artifact gate 이후 후속 후보를 별도 설계 단위로 재평가한다.

- [x] io_uring endpoint diagnostics snapshot surface 를 SAEA/RIO와 맞췄다.
  - 범위: `src/Hps.Transport.IoUring/IoUringTransport.cs`,
    `tests/Hps.Transport.IoUring.Tests/IoUringTransportUdpTests.cs`, 상태/결정 문서.
  - Red: `GetEndpointSnapshots_WhenUdpEndpointIsRegistered_ReturnsUdpSnapshotAndRemovesItAfterClose`가
    `ITransportEndpointDiagnostics` cast 실패로 깨지는 것을 확인했다.
  - Green: `IoUringTransport`가 `ITransportEndpointDiagnostics`를 구현하고,
    TCP connection/UDP endpoint registry snapshot 을 `EndpointSnapshot[]`로 반환한다.
  - 검증: focused diagnostics test 1개 통과, `Hps.Transport.IoUring.Tests` 52개 통과.
  - 비고: `ITransport` 기본 계약은 넓히지 않았고, default promotion/fixed registration/zero-copy/receive window 확장은 열지 않았다.

- [x] io_uring UDP pump 로컬 계약 보강 테스트를 추가했다.
  - 범위: `tests/Hps.Transport.IoUring.Tests/IoUringUdpEndpointShapeTests.cs`,
    `tests/Hps.Transport.IoUring.Tests/IoUringUdpMessageShapeTests.cs`.
  - 결과: `TrySendTo` 성공 시 queued transport ref 가 endpoint close 까지 유지되는지,
    closed endpoint/IPv6 remote 거절 시 caller ref 가 그대로 남는지,
    drop-oldest diagnostics 와 message buffer send metadata/Dispose 경계가 유지되는지 검증한다.
  - 비고: 프로덕션 코드는 변경하지 않았다. D140 제외 범위인 fixed registration, zero-copy send,
    receive window depth 확장, default backend promotion 은 열지 않았다.
  - 검증: `dotnet test tests\Hps.Transport.IoUring.Tests\Hps.Transport.IoUring.Tests.csproj --no-restore -v minimal`
    51개 통과.
  - 다음: 사용자 push 이후 원격 `iouring-linux-contract` artifact 로 실제 Linux UDP syscall path 를 검토한다.

- [x] Phase 6 io_uring UDP pump 구현 계획 Task 5 State Docs And Verification 을 수행했다.
  - 범위: root/archive state docs, D140 decision presence, 최종 검증.
  - 결과: Task 1~4 구현 완료 상태를 현재 실행 지점에 반영하고,
    다음 실행 항목을 원격 `iouring-linux-contract` UDP artifact 검토로 넘겼다.
  - 검증: D140 root/archive scan 통과, solution build 경고 0/오류 0,
    solution tests 426개 통과, `git diff --check` 통과.
  - 다음: 사용자 push 이후 원격 Linux workflow artifact 로 UDP native syscall path 를 검토한다.

- [x] Phase 6 io_uring UDP pump 구현 계획 Task 4 UDP Send Pump And Ownership 을 TDD로 구현했다.
  - 범위: `IoUringTransport`, `IoUringTransportUdpTests`, `IoUringUdpEndpointShapeTests`.
  - Red: `IoUringTransportUdpTests` send shape test 가 `TrySendTo` override/send pump 경계 부재로 실패하는 것을 확인했다.
  - Green: `TrySendTo(...)`, endpoint send loop, one-deep `sendmsg` submit/wait path,
    completion/drop/close ref 반환을 추가했다.
  - 검증: focused Task 4 tests 3개 통과, `Hps.Transport.IoUring.Tests` 46개 통과,
    solution build 경고 0/오류 0, solution tests 426개 통과, `git diff --check` 통과.
  - 비고: Windows/non-Linux 에서는 echo loopback native path 가 capability gate 로 early-return 한다.
  - 다음: Task 5 State Docs And Verification 을 수행한다.

- [x] Phase 6 io_uring UDP pump 구현 계획 Task 3 UDP Bind And Receive Pump 를 TDD로 구현했다.
  - 범위: `IoUringTransport`, `IoUringTransportUdpTests`.
  - Red: `IoUringTransportUdpTests` shape test 가 `_udpEndpoints`/receive pump 경계 부재로 실패하는 것을 확인했다.
  - Green: IPv4 UDP bind, endpoint 등록/해제, transport stop dispose, one-deep `recvmsg` receive loop,
    datagram handler dispatch, receive failure close notify 를 추가했다.
  - 검증: focused `IoUringTransportUdpTests` 2개 통과, `Hps.Transport.IoUring.Tests` 43개 통과,
    solution build 경고 0/오류 0, solution tests 423개 통과, `git diff --check` 통과.
  - 비고: Windows/non-Linux 에서는 capability gate 로 실제 native UDP loopback 이 early-return 한다.
  - 다음: Task 4 UDP Send Pump And Ownership 을 시작한다.

- [x] Phase 6 io_uring UDP pump 구현 계획 Task 2 UDP Endpoint Resource And Message Buffer 를 TDD로 구현했다.
  - 범위: `IoUringUdpMessageBuffer`, `IoUringUdpEndpoint`, `IoUringUdpEndpointShapeTests`.
  - Red: `IoUringUdpEndpointShapeTests` 2개가 endpoint/message buffer type 부재로 실패하는 것을 확인했다.
  - Green: UDP endpoint resource, pinned message header/iovec/sockaddr scratch, receive pool,
    pending send queue, close drain ownership 을 추가했다.
  - 검증: focused `IoUringUdpEndpointShapeTests` 2개 통과, `Hps.Transport.IoUring.Tests` 41개 통과,
    solution build 경고 0/오류 0, solution tests 421개 통과, `git diff --check` 통과.
  - 다음: Task 3 UDP Bind And Receive Pump 를 시작한다.

- [x] Phase 6 io_uring UDP pump 설계와 TDD 구현 계획을 작성했다.
  - 범위: D139 이후 io_uring UDP bind/receive/send pump 후속 후보.
  - 결과: `docs/superpowers/specs/2026-06-30-iouring-udp-pump-design.md`와
    `docs/superpowers/plans/2026-06-30-iouring-udp-pump.md`를 작성했다.
  - 결정: D140으로 UDP v1은 IPv4 one-deep `recvmsg`/`sendmsg` pump 로 제한했다.
  - 제외: IPv6 direct io_uring UDP, receive window depth 2 이상, fixed payload registration cache,
    zero-copy send, default backend promotion.
  - 다음: 구현 계획 Task 1 Native UDP Message Shape 를 TDD로 시작한다.

- [x] Phase 6 io_uring UDP pump 구현 계획 Task 1 Native UDP Message Shape 를 TDD로 구현했다.
  - 범위: `IoUringNative`, `IoUringQueue`, `IoUringOperationKind`, `IoUringSockaddr`, `IoUringUdpMessageShapeTests`.
  - Red: `IoUringUdpMessageShapeTests` 2개가 missing type/member 로 실패하는 것을 확인했다.
  - Green: `IORING_OP_RECVMSG`/`IORING_OP_SENDMSG`, Linux `IoUringMessageHeader`,
    queue message submit helper, IPv4 sockaddr encode/decode helper 를 추가했다.
  - 검증: focused `IoUringUdpMessageShapeTests` 2개 통과,
    `Hps.Transport.IoUring.Tests` 39개 통과, solution build 경고 0/오류 0.
  - 다음: Task 2 UDP Endpoint Resource And Message Buffer 를 시작한다.

- [x] 원격 `iouring-linux-contract` workflow 실행 결과 artifact 를 검토했다.
  - 범위: GitHub Actions run `28411459951`, artifact `iouring-linux-contract-2026-06-30-github-28411459951-1`.
  - 결과: workflow conclusion success, test exit code 0, TRX counters total 37 / passed 37 / failed 0.
  - evidence: `IoUringCapabilityEvidenceTests` output 은 `io_uring capability status: Available`,
    OS `Ubuntu 24.04.4 LTS`, architecture `X64`를 기록했다.
  - 의미: TCP receive/send loopback tests 가 available path 에서 통과했으므로,
    D138 Linux contract gate 와 Deferred Backlog 의 Linux available host TCP loopback 검증을 완료 처리한다.
  - 다음: Phase 6 io_uring UDP pump 설계와 TDD 구현 계획을 작성한다.

- [x] Linux io_uring contract gate Task 3 state documents and decision 을 수행했다.
  - 범위: `CURRENT_PLAN.md`, `TODOS.md`, `CHANGELOG_AGENT.md`, `DECISIONS.md`,
    `docs/agent-state/changelog/2026-06.md`, `docs/agent-state/decisions/2026-06.md`.
  - 결과: D138 gate 완료 상태, workflow local validation, remote artifact 미실행 상태를 root/archive 문서에 정리했다.
  - 비고: 원격 `workflow_dispatch` 실행 결과가 아직 없으므로 Linux actual syscall loopback 검증은 완료되지 않았다.
  - 검증: D138/state consistency scan, `git diff --check`, solution build 경고 0/오류 0, solution tests 417개 통과.
  - 다음: 원격 workflow artifact 가 생기면 결과를 검토한다.

- [x] Linux io_uring contract gate Task 2 Linux contract workflow 를 추가했다.
  - 범위: `.github/workflows/iouring-linux-contract.yml`, root 상태 문서.
  - 결과: `workflow_dispatch` 전용 `ubuntu-latest` workflow 를 추가했다.
    workflow 는 io_uring tests TRX, `dotnet-info.txt`, `summary.md`를 upload artifact 로 남긴다.
  - 비고: capability unavailable 은 workflow failure 가 아니다. restore/build/test command 실패만 failure 로 취급한다.
    원격 Linux workflow 실행은 아직 수행하지 않았다.
  - 검증: workflow marker scan 통과, `git diff --check` 통과,
    solution build 경고 0/오류 0, solution tests 417개 통과.
  - 다음: Task 3 state documents and decision 을 수행한다.

- [x] Linux io_uring contract gate Task 1 capability evidence test 를 추가했다.
  - 범위: `tests/Hps.Transport.IoUring.Tests/IoUringCapabilityEvidenceTests.cs`, root 상태 문서.
  - 결과: `IoUringCapabilityProbe.GetStatus()` 결과, OS description, OS/process architecture 를 xUnit output 으로 남기는
    test-only evidence surface 를 추가했다.
  - 비고: production transport 코드는 변경하지 않았다. 실제 Linux available host loopback 검증은 아직 Deferred Backlog 로 남아 있다.
  - 검증: focused `IoUringCapabilityEvidenceTests` 1개 통과,
    `dotnet test tests\Hps.Transport.IoUring.Tests\Hps.Transport.IoUring.Tests.csproj --no-build --no-restore -v minimal` 37개 통과,
    solution build 경고 0/오류 0, solution tests 417개 통과, `git diff --check` 통과.
  - 다음: Task 2 Linux contract workflow 를 추가한다.

- [x] Phase 6 io_uring 후속 후보를 재평가하고 Linux contract gate 설계/구현 계획을 작성했다.
  - 범위: D133~D137, `PLAN.md` Phase 6, `docs/superpowers/specs/2026-06-29-iouring-tcp-first-pump-design.md`,
    `src/Hps.Transport.IoUring/`, `tests/Hps.Transport.IoUring.Tests/`.
  - 결과: D138로 UDP pump/zero-copy 최적화 전에 Linux contract evidence gate 를 먼저 두기로 했다.
  - 산출물: `docs/superpowers/specs/2026-06-29-iouring-linux-contract-gate-design.md`,
    `docs/superpowers/plans/2026-06-29-iouring-linux-contract-gate.md`.
  - 비고: 실제 Linux available host 검증은 아직 실행하지 않았다. 다음 구현은 capability evidence test 부터 시작한다.
  - 검증: spec/plan self-review, D138 state doc 연결, `git diff --check`.

- [x] Phase 6 TCP-first io_uring queue/pump Task 7 state documents and full verification 을 수행했다.
  - 범위: `CURRENT_PLAN.md`, `TODOS.md`, `CHANGELOG_AGENT.md`, `DECISIONS.md`,
    `docs/agent-state/changelog/2026-06.md`, `docs/agent-state/decisions/2026-06.md`.
  - 결과: D137로 TCP-first io_uring pump 구현 boundary 를 수락했다.
    shared `IoUringQueue`/`IoUringCompletionLoop`, reusable operation context, 공통 `TransportConnection`
    send queue 재사용이 현재 구현 기준이다.
  - 비고: 실제 Linux available host receive/send loopback 은 현재 Windows 환경에서 직접 실행하지 못하므로
    Deferred Backlog 의 `P1_SOON` 환경 의존 검증으로 유지한다.
  - 검증: `dotnet build HighPerformanceSocket.slnx --no-restore -v minimal` 경고 0/오류 0,
    `dotnet test HighPerformanceSocket.slnx --no-build --no-restore -v minimal` 416개 통과,
    `git diff --check` 통과.
  - 다음: Phase 6 io_uring 후속 후보를 재평가하고 다음 설계/구현 단위를 정한다.

- [x] Phase 6 TCP-first io_uring queue/pump Task 6 TCP send pump and ownership 을 TDD로 구현했다.
  - 범위: `src/Hps.Transport.IoUring/IoUringQueue.cs`,
    `src/Hps.Transport.IoUring/IoUringTransport.cs`,
    `tests/Hps.Transport.IoUring.Tests/IoUringSendPumpShapeTests.cs`,
    `tests/Hps.Transport.IoUring.Tests/IoUringTransportTcpTests.cs`, root 상태 문서.
  - 결과: SEND SQE submit helper 와 `TransportConnection` pending queue 기반 send loop 를 추가했다.
    length-prefix 는 pinned prefix block 을 먼저 보내고, payload 는 `TransportSendBuffer` slice metadata 를 따른다.
  - Red: send pump queue/transport shape 부재를 reflection 기반 `Assert.NotNull()` failure 1개로 확인했다.
  - Green: focused `IoUringSendPumpShapeTests` 1개와 `Hps.Transport.IoUring.Tests` 전체 36개 통과.
  - 비고: 현재 Windows 검증에서는 Linux-gated send loopback 이 early-return 한다. 실제 Linux available host 검증은 Deferred Backlog 로 남겼다.
  - 검증: `dotnet build HighPerformanceSocket.slnx --no-restore -v minimal` 경고 0/오류 0,
    `dotnet test HighPerformanceSocket.slnx --no-build --no-restore -v minimal` 416개 통과,
    `git diff --check` 통과.
  - 후속: Task 7 state documents and full verification 은 D137 문서 단위로 완료됐다.

- [x] Phase 6 TCP-first io_uring queue/pump Task 5 TCP receive pump 를 TDD로 구현했다.
  - 범위: `src/Hps.Transport.IoUring/IoUringQueue.cs`,
    `src/Hps.Transport.IoUring/IoUringCompletionLoop.cs`,
    `src/Hps.Transport.IoUring/IoUringTcpConnectionResource.cs`,
    `src/Hps.Transport.IoUring/IoUringTransport.cs`,
    `tests/Hps.Transport.IoUring.Tests/IoUringReceivePumpShapeTests.cs`,
    `tests/Hps.Transport.IoUring.Tests/IoUringTransportTcpTests.cs`, root 상태 문서.
  - 결과: RECV SQE submit helper, CQE drain helper, completion loop polling drain, TCP receive loop을 추가했다.
    positive completion 은 receive handler 로 전달하고, 0/음수 completion 과 handler 예외는 connection close notify 로 수렴한다.
  - Red: receive pump queue/transport shape 부재를 reflection 기반 `Assert.NotNull()` failure 1개로 확인했다.
  - Green: focused `IoUringReceivePumpShapeTests` 1개와 `Hps.Transport.IoUring.Tests` 전체 34개 통과.
  - 비고: 현재 Windows 검증에서는 Linux-gated loopback 이 early-return 한다. 실제 Linux available host 검증은 Deferred Backlog 로 남겼다.
  - 검증: `dotnet build HighPerformanceSocket.slnx --no-restore -v minimal` 경고 0/오류 0,
    `dotnet test HighPerformanceSocket.slnx --no-build --no-restore -v minimal` 414개 통과,
    `git diff --check` 통과.
  - 다음: Task 6 TCP send pump and ownership 을 TDD로 구현한다.

- [x] Phase 6 TCP-first io_uring queue/pump Task 4 TCP resource and listener wiring 을 TDD로 구현했다.
  - 범위: `src/Hps.Transport.IoUring/IoUringConnectionListener.cs`,
    `src/Hps.Transport.IoUring/IoUringTcpConnectionResource.cs`,
    `src/Hps.Transport.IoUring/IoUringTransport.cs`,
    `src/Hps.Transport/Properties/AssemblyInfo.cs`,
    `tests/Hps.Transport.IoUring.Tests/IoUringTransportTcpTests.cs`, root 상태 문서.
  - 결과: TCP listener/resource skeleton 을 추가하고, resource dispose 가 pinned block 반환과 registry unregister 를 수행하게 했다.
    `IoUringTransport`는 Linux capability available 상태에서 queue/registry/completion loop 를 준비하고,
    non-Linux 는 기존처럼 명시적 `NotSupportedException`으로 수렴한다.
  - Red: TCP listener/resource type 부재를 reflection 기반 `Assert.NotNull()` failure 2개로 확인했다.
  - Green: focused `IoUringTransportTcpTests` 4개와 `Hps.Transport.IoUring.Tests` 전체 32개 통과.
  - 검증: `dotnet build HighPerformanceSocket.slnx --no-restore -v minimal` 경고 0/오류 0,
    `dotnet test HighPerformanceSocket.slnx --no-build --no-restore -v minimal` 412개 통과,
    `git diff --check` 통과.
  - 다음: Task 5 TCP receive pump 를 TDD로 구현한다.

- [x] Phase 6 TCP-first io_uring queue/pump Task 3 shared completion loop boundary 를 TDD로 구현했다.
  - 범위: `src/Hps.Transport.IoUring/IoUringCompletionLoop.cs`,
    `tests/Hps.Transport.IoUring.Tests/IoUringCompletionLoopTests.cs`, root 상태 문서.
  - 결과: CQE `user_data` token 을 registry context 로 dispatch 하는 `IoUringCompletionLoop` 경계를 추가했다.
    현재 단계에서는 native CQ drain thread 없이 pure dispatch 와 lifecycle shell 만 구현했다.
  - Red: completion loop type 부재를 reflection 기반 `Assert.NotNull()` failure 4개로 확인했다.
  - Green: focused `IoUringCompletionLoopTests` 4개와 `Hps.Transport.IoUring.Tests` 전체 28개 통과.
  - 검증: `dotnet build HighPerformanceSocket.slnx --no-restore -v minimal` 경고 0/오류 0,
    `dotnet test HighPerformanceSocket.slnx --no-build --no-restore -v minimal` 408개 통과,
    `git diff --check` 통과.
  - 다음: Task 4 TCP resource and listener wiring 을 TDD로 구현한다.

- [x] Phase 6 TCP-first io_uring queue/pump Task 2 operation registry and completion context 를 TDD로 구현했다.
  - 범위: `src/Hps.Transport.IoUring/IoUringOperationKind.cs`,
    `src/Hps.Transport.IoUring/IoUringCompletion.cs`,
    `src/Hps.Transport.IoUring/IoUringOperationContext.cs`,
    `src/Hps.Transport.IoUring/IoUringOperationRegistry.cs`,
    `tests/Hps.Transport.IoUring.Tests/IoUringOperationRegistryTests.cs`, root 상태 문서.
  - 결과: CQE `user_data` token 을 managed operation context 로 라우팅하는 registry 를 추가했다.
    context 는 `WaitAsync` 이후 completion 을 정확히 한 번만 허용하고, `Reset`으로 재사용 상태를 준비한다.
  - Red: operation registry/context type 부재를 reflection 기반 `Assert.NotNull()` failure 6개로 확인했다.
  - Green: focused `IoUringOperationRegistryTests` 6개와 `Hps.Transport.IoUring.Tests` 전체 24개 통과.
  - 검증: `dotnet build HighPerformanceSocket.slnx --no-restore -v minimal` 경고 0/오류 0,
    `dotnet test HighPerformanceSocket.slnx --no-build --no-restore -v minimal` 404개 통과,
    `git diff --check` 통과.
  - 다음: Task 3 shared completion loop boundary 를 TDD로 구현한다.

- [x] Phase 6 TCP-first io_uring queue/pump Task 1 native SQE/CQE/enter shape 를 TDD로 구현했다.
  - 범위: `src/Hps.Transport.IoUring/IoUringNative.cs`,
    `tests/Hps.Transport.IoUring.Tests/IoUringSubmissionShapeTests.cs`, root 상태 문서.
  - 결과: TCP `SEND`/`RECV` opcode, `io_uring_enter` wrapper, SQE/CQE ABI struct 를 추가했다.
  - Red: SQE/CQE type 부재를 reflection 기반 `Assert.NotNull()` failure 1개로 확인했다.
  - Green: focused `IoUringSubmissionShapeTests` 1개와 `Hps.Transport.IoUring.Tests` 전체 18개 통과.
  - 검증: `dotnet build HighPerformanceSocket.slnx --no-restore -v minimal` 경고 0/오류 0,
    `dotnet test HighPerformanceSocket.slnx --no-build --no-restore -v minimal` 398개 통과,
    `git diff --check` 통과.
  - 다음: Task 2 operation registry and completion context 를 TDD로 구현한다.

- [x] Phase 6 TCP-first io_uring queue/pump 설계와 구현 계획을 작성했다.
  - 범위: `docs/superpowers/specs/2026-06-29-iouring-tcp-first-pump-design.md`,
    `docs/superpowers/plans/2026-06-29-iouring-tcp-first-pump.md`,
    `CURRENT_PLAN.md`, `TODOS.md`, `CHANGELOG_AGENT.md`, `DECISIONS.md`,
    `docs/agent-state/changelog/2026-06.md`, `docs/agent-state/decisions/2026-06.md`.
  - 결과: D136으로 transport shared queue/completion loop 와 reusable operation context 설계를 채택했다.
    구현 계획은 native shape, operation registry, completion loop, TCP resource/listener, receive pump, send pump,
    state docs/full verification 의 7개 단위로 나눴다.
  - 검증: spec/plan placeholder scan, type consistency scan, `git diff --check` 통과,
    solution build 경고 0/오류 0, solution tests 397개 통과.
  - 다음: implementation plan Task 1 native SQE/CQE/enter shape 를 TDD로 구현한다.

- [x] Phase 6 Linux io_uring native wrapper shape Task 5 state documents and full verification 을 수행했다.
  - 범위: `CURRENT_PLAN.md`, `TODOS.md`, `CHANGELOG_AGENT.md`, `DECISIONS.md`,
    `docs/agent-state/changelog/2026-06.md`, `docs/agent-state/decisions/2026-06.md`.
  - 결과: D135로 native wrapper boundary 완료와 TCP/UDP pump 후속 분리를 기록하고,
    다음 실행 지점을 TCP-first io_uring queue/pump 설계로 넘겼다.
  - 검증: `dotnet build HighPerformanceSocket.slnx --no-restore -v minimal` 경고 0/오류 0,
    `dotnet test HighPerformanceSocket.slnx --no-build --no-restore -v minimal` 397개 통과,
    `git diff --check` 통과.
  - 다음: TCP-first io_uring queue/pump 설계를 작성한다.

- [x] Phase 6 Linux io_uring native wrapper shape Task 4 fixed buffer registration owner boundary 를 TDD로 구현했다.
  - 범위: `src/Hps.Transport.IoUring/IoUringNative.cs`,
    `src/Hps.Transport.IoUring/IoUringQueue.cs`,
    `src/Hps.Transport.IoUring/IoUringRegisteredBufferSet.cs`,
    `tests/Hps.Transport.IoUring.Tests/IoUringRegisteredBufferSetTests.cs`, root 상태 문서.
  - 결과: `io_uring_register` buffers/unregister wrapper, queue fd 내부 접근자,
    managed buffer pinning 과 registration 수명을 함께 소유하는 registration owner boundary 를 추가했다.
  - Red: `IoUringRegisteredBufferSet` type 부재를 reflection 기반 `Assert.NotNull()` failure 2개로 확인했다.
  - Green: focused `IoUringRegisteredBufferSetTests` 2개와 `Hps.Transport.IoUring.Tests` 전체 17개 통과.
  - 검증: 전체 build/test/diff check 는 커밋 전 표준 검증으로 수행한다.
  - 다음: Task 5 state documents and full verification 을 수행한다.

- [x] Phase 6 Linux io_uring native wrapper shape Task 3 capability probe wiring 을 TDD로 구현했다.
  - 범위: `src/Hps.Transport.IoUring/IoUringCapabilityProbe.cs`,
    `tests/Hps.Transport.IoUring.Tests/IoUringCapabilityProbeTests.cs`, root 상태 문서.
  - 결과: `IoUringCapabilityProbe.GetStatus()`가 Linux 에서 `IoUringQueue.TryCreateForProbe(2)`를 사용해
    작은 ring setup/close probe 결과를 capability status 로 수렴한다.
  - Red: probe result mapping internal overload 부재를 `Assert.NotNull()` failure 1개로 확인했다.
  - Green: focused `IoUringCapabilityProbeTests` 5개와 `Hps.Transport.IoUring.Tests` 전체 15개 통과.
  - 검증: 전체 build/test/diff check 는 커밋 전 표준 검증으로 수행한다.
  - 다음: Task 4 fixed buffer registration owner boundary 를 TDD로 구현한다.

- [x] Phase 6 Linux io_uring native wrapper shape Task 2 queue setup owner 를 TDD로 구현했다.
  - 범위: `src/Hps.Transport.IoUring/IoUringNative.cs`,
    `src/Hps.Transport.IoUring/IoUringSafeHandle.cs`, `IoUringMemoryMap.cs`, `IoUringQueue.cs`,
    `tests/Hps.Transport.IoUring.Tests/IoUringQueueTests.cs`, root 상태 문서.
  - 결과: setup fd close owner, mmap owner, queue setup/mmap owner, queue probe result 를 추가했다.
    non-Linux 는 setup syscall 로 진입하지 않고, Linux 에서만 작은 ring setup/mmap probe 를 시도할 수 있다.
  - Red: `IoUringQueue` type 부재를 reflection 기반 `Assert.NotNull()` failure 2개로 확인했다.
  - Green: focused `IoUringQueueTests` 3개와 `Hps.Transport.IoUring.Tests` 전체 13개 통과.
  - 검증: `dotnet build HighPerformanceSocket.slnx --no-restore -v minimal` 경고 0/오류 0,
    `dotnet test HighPerformanceSocket.slnx --no-build --no-restore -v minimal` 393개 통과,
    `git diff --check` 통과.
  - 다음: Task 3 capability probe wiring 을 TDD로 구현한다.

- [x] Phase 6 Linux io_uring native wrapper shape Task 1 native ABI shell/platform guard 를 TDD로 구현했다.
  - 범위: `src/Hps.Transport.IoUring/IoUringNative.cs`,
    `src/Hps.Transport.IoUring/Properties/AssemblyInfo.cs`,
    `tests/Hps.Transport.IoUring.Tests/IoUringNativeShapeTests.cs`, root 상태 문서.
  - 결과: internal `IoUringNative` type, `GetPlatformStatus()`, `ThrowIfUnsupportedPlatform()`을 추가했다.
    non-Linux 는 `UnsupportedOperatingSystem`과 `NotSupportedException`으로 수렴하고,
    Linux x64/arm64만 후속 native setup 후보로 둔다.
  - Red: `IoUringNative` type 부재를 reflection 기반 `Assert.NotNull()` failure 3개로 확인했다.
  - Green: focused `IoUringNativeShapeTests` 3개와 `Hps.Transport.IoUring.Tests` 전체 10개 통과.
  - 검증: `dotnet build HighPerformanceSocket.slnx --no-restore -v minimal` 경고 0/오류 0,
    `dotnet test HighPerformanceSocket.slnx --no-build --no-restore -v minimal` 390개 통과,
    `git diff --check` 통과.
  - 다음: Task 2 queue setup owner 를 TDD로 구현한다.

- [x] Phase 6 Linux io_uring native syscall wrapper shape 설계와 구현 계획을 완료했다.
  - 범위: D133, RIO native wrapper 기존 경계, `src/Hps.Transport.IoUring/`, `tests/Hps.Transport.IoUring.Tests/`.
  - 결과: `docs/superpowers/specs/2026-06-29-iouring-native-wrapper-shape-design.md`와
    `docs/superpowers/plans/2026-06-29-iouring-native-wrapper-shape.md`를 작성했다.
  - 결정: D134로 native adapter(`IoUringNative`), fd/mmap owner(`IoUringQueue`),
    fixed buffer registration owner(`IoUringRegisteredBufferSet`) 분리를 채택했다.
  - 검증: spec/plan placeholder scan, type consistency scan, `git diff --check`로 확인한다.
  - 다음: 구현 계획 Task 1 native ABI shell/platform guard 를 TDD로 구현한다.

- [x] Phase 6 Linux io_uring boundary Task 3 state docs/full verification 을 완료했다.
  - 범위: `CURRENT_PLAN.md`, `TODOS.md`, `CHANGELOG_AGENT.md`, `DECISIONS.md`,
    `docs/agent-state/changelog/2026-06.md`, `docs/agent-state/decisions/2026-06.md`.
  - 결과: D133으로 Phase 6 첫 io_uring 구현을 skeleton/probe/unsupported boundary 까지로 제한하고,
    native syscall wrapper 와 TCP/UDP pump 는 후속 task 로 분리했다.
  - 검증: `dotnet build HighPerformanceSocket.slnx --no-restore -v minimal` 경고 0/오류 0,
    `dotnet test HighPerformanceSocket.slnx --no-build --no-restore -v minimal` 387개 통과,
    `git diff --check` 통과.
  - 다음: Linux io_uring native syscall wrapper shape 설계를 작성한다.

- [x] Phase 6 Linux io_uring boundary Task 2 `IoUringTransport` lifecycle/unsupported boundary 를 TDD로 구현했다.
  - 범위: `src/Hps.Transport.IoUring/IoUringTransport.cs`,
    `tests/Hps.Transport.IoUring.Tests/IoUringTransportTests.cs`, root 상태 문서.
  - 결과: opt-in `IoUringTransport` root type 을 추가했다.
    `StartAsync`/`StopAsync`는 native 자원 없이 lifecycle shell 만 제공하고,
    TCP listen/connect 와 UDP bind 는 현재 capability 상태에 맞춰 명시적 `NotSupportedException`으로 수렴한다.
  - Red: `IoUringTransport` type 부재를 reflection 기반 `Assert.NotNull()` failure 4개로 확인했다.
  - Green: focused `IoUringTransportTests` 4개와 `Hps.Transport.IoUring.Tests` 전체 7개 통과.
  - 비고: native SQ/CQ, mmap, fixed buffer registration, TCP/UDP pump 는 Task 3 이후 별도 설계/구현 범위다.

- [x] Phase 6 Linux io_uring boundary Task 1 project skeleton/capability probe 를 TDD로 구현했다.
  - 범위: `src/Hps.Transport.IoUring/`, `tests/Hps.Transport.IoUring.Tests/`, `HighPerformanceSocket.slnx`.
  - 결과: `IoUringCapabilityStatus`, `IoUringCapabilityProbe.GetStatus()`를 추가했다.
    non-Linux 는 `UnsupportedOperatingSystem`, Linux 는 native syscall probe 전까지 `Unavailable`로 반환한다.
    `TransportFactory.CreateDefault()`는 계속 `SaeaTransport`를 반환한다.
  - Red: source project 없이 reflection tests 를 먼저 추가해 `Assert.NotNull()` failure 2개를 확인했다.
  - Green: source project/probe/status 추가 후 `Hps.Transport.IoUring.Tests` 3개 통과.
  - 비고: `IoUringTransport`, native P/Invoke, TCP/UDP pump 는 Task 2 이후 범위다.

- [x] Phase 6 Linux io_uring boundary 첫 구현 계획을 작성했다.
  - 범위: D132 spec, `PLAN.md` Phase 6, RIO skeleton/probe 기존 패턴, current project layout.
  - 결과: `docs/superpowers/plans/2026-06-29-iouring-boundary.md`를 작성했다.
    계획은 Task 1 project skeleton/capability probe, Task 2 `IoUringTransport` lifecycle/unsupported boundary,
    Task 3 state docs/full verification 으로 나뉜다.
  - 비고: 첫 Red가 compile failure 가 아니라 reflection assertion failure 로 나도록 test project reference 순서를 명시했다.

- [x] D131 이후 다음 실행 후보를 재평가했다.
  - 범위: D131 CI baseline 2-session 상태, D090/D095/D119/D122/D125/D128 결정,
    `PLAN.md` Phase 6, deferred backlog.
  - 결과: CI gate 승격, RIO default promotion/full IPv6, server-level diagnostics public API는 지금 열지 않는다.
    다음 후보는 Phase 6 Linux io_uring backend boundary 설계와 첫 구현 계획으로 정했다(D132).
  - 비고: 현재 Windows 환경에서는 Linux native integration 을 바로 검증할 수 없으므로,
    첫 구현 후보는 project skeleton, capability probe, non-Linux unsupported boundary, default SAEA 유지 regression 으로 제한한다.
  - 산출물: `docs/superpowers/specs/2026-06-29-phase6-iouring-boundary-next-candidate-design.md`.

- [x] D127/D130 push-triggered CI artifact 결과를 검증하고 D095 기준으로 두 번째 CI baseline 을 채택했다.
  - 범위: GitHub Actions run `28350456434`, uploaded artifact,
    `docs/benchmarks/baselines/runners/ci-windows-x64-01/2026-06-29/session-01/`,
    `docs/benchmarks/baselines/runners/ci-windows-x64-01/history.json`,
    `docs/benchmarks/baselines/index.md`, D095/D131 상태/결정 문서.
  - 결과: run `28350456434`는 push event, head SHA `384f3c5932c1a2b22ff92116068bfcda22f56778`, conclusion success 다.
    upload artifact 는 raw report 6개, `summary.json`, `summary.md`, `history.json`, `history.md`, `envelope.json`,
    `envelope.md`를 모두 포함했다.
  - 채택: raw report 6개를 `ci-windows-x64-01/2026-06-29/session-01`로 복사하고,
    repository 경로 기준 summary/date history/runner history 를 재생성했다.
    runner root history 는 2-session, hard-passed true, warning-count 0, comparison-compatible true 다.
  - 비고: upload artifact envelope 는 이전 1-session CI reference 대비 p99 upper-bound signal 2개를 기록했지만,
    D125/D127 기준 report-only 이므로 workflow failure, warning-count, 채택 차단 조건으로 처리하지 않았다.
  - 검증: artifact file check, D095 adoption checklist, summary/history CLI 재생성 통과.

- [x] D127 이후 Phase 4 다음 구현 후보를 다시 확정했다.
  - 범위: `.claude/review/2026-06-29-next-scope-decision-review.md`, D123~D127,
    `docs/superpowers/specs/2026-06-29-phase4-next-candidate-after-d127.md`, root 상태/결정 문서.
  - 결과: 기존 review 의 local runner 2-date-root 전제는 stale 로 판정했다.
    D123~D127 이후 RIO full IPv6/default promotion 과 server diagnostics 는 계속 deferred 로 유지하고,
    다음 후보를 D127 workflow 의 push-triggered CI artifact 검증으로 좁혔다(D128).
  - 비고: envelope signal 은 계속 report-only 이며, CI artifact 자동 baseline 채택이나 hard gate 승격으로 이어지지 않는다.
    이후 `git push origin master`를 직접 실행하려 했으나 현재 도구 정책에서 거부되어,
    remote CI 검증은 원격 push 이후로 남겼다.
  - 검증: 최신 review/state/backlog 대조, `git diff --check` 통과,
    solution build 경고 0/오류 0, solution tests 379개 통과.

- [x] CI benchmark workflow 에 report-only envelope comparison artifact 를 연결했다.
  - 범위: `.github/workflows/benchmark-artifacts.yml`,
    `tests/Hps.Benchmarks.Tests/BenchmarkArtifactWorkflowTests.cs`,
    `docs/superpowers/specs/2026-06-29-ci-envelope-comparison-artifact-design.md`,
    `docs/superpowers/specs/2026-06-29-runner-profile-warning-envelope-model-design.md`,
    `DECISIONS.md`, `docs/agent-state/decisions/2026-06.md`, root 상태 문서.
  - 결과: D127로 CI workflow 가 repository reference history 존재 시
    `--compare-baseline-envelope`를 실행해 `envelope.json`과 `envelope.md`를 upload date root 에 포함한다.
    reference history 가 없으면 bootstrap 상태로 보고 skip 한다.
  - 비고: envelope mismatch/signal 은 D125 기준 report-only 이며 CI failure, warning-count, warning-as-failure 로 승격하지 않는다.
  - Red: workflow 정적 테스트가 기존 workflow 의 envelope step 부재로 `Assert.True()` 실패했다.
  - Green: focused workflow test 1개 통과.
    현재 `ci-windows-x64-01` repository baseline 을 reference 로 한 CLI smoke 도 exit code 0,
    `envelope-compatible=true`, `envelope-signal-count=0`으로 통과했다.
  - 검증: `git diff --check` 통과, solution build 경고 0/오류 0,
    solution tests 379개 통과.
  - 다음: D127 이후 Phase 4 다음 후보를 다시 확정한다.

- [x] SDK 선택 재현성 hardening 을 수행했다.
  - 범위: `global.json`, `DECISIONS.md`, `docs/agent-state/decisions/2026-06.md`, root 상태 문서.
  - 결과: 저장소 루트 기본 `dotnet` SDK 선택을 9.0.314 계열로 고정했다.
    stale restore 산출물은 `dotnet restore --ignore-failed-sources`로 현재 사용자 package root 기준으로 재생성했다.
  - 원인: `global.json` 부재 상태에서 기본 SDK 10.0.203이 선택됐고,
    이전 실행 환경 package root 를 가리키는 `project.assets.json` 때문에 BenchmarkDotNet transitive analyzer metadata `CS0006`가 재현됐다.
  - 검증: 기본 `dotnet --version` 9.0.314 확인,
    기본 `dotnet build HighPerformanceSocket.slnx --no-restore` 경고 0/오류 0,
    `dotnet test HighPerformanceSocket.slnx --no-build --no-restore -v minimal` 전체 378개 통과,
    `git diff --check` 통과.
  - 다음: D127 이후 상태를 포함해 최신 review/backlog 를 다시 대조하고 다음 실행 후보를 확정한다.

- [x] runner/profile scoped envelope comparison command 구현 self-review 와 schema 보정을 수행했다.
  - 범위: D125 spec/plan, envelope comparison writer/model/tests/Program output, review 문서, root 상태 문서.
  - 결과: D125와 다른 field name 을 쓰던 `reference-source-path`, `candidate-source-path`, `mismatches`를
    `reference-history-path`, `candidate-path`, `envelope-mismatches`로 정렬하고,
    `candidate-kind`, reference/candidate summary count, signal `code`를 추가했다.
  - Red: writer schema test 가 기존 `reference-history-path` 누락으로 `KeyNotFoundException` 실패했다.
  - Green: writer tests 4개, Program tests 2개, envelope 관련 tests 16개 통과.
    실제 local runner artifact CLI smoke 도 exit code 0, schema field/count 확인으로 통과했다.
  - 검증: `git diff --check` 통과, .NET 9.0.314 MSBuild 기준 solution build 통과
    (`NU1900` vulnerability feed 조회 경고 1건), solution tests 378개 통과.
  - 다음: SDK 선택 재현성 hardening 을 수행한다.

- [x] runner/profile scoped envelope comparison Task 4 writer/Program wiring 을 구현했다.
  - 범위: `BaselineEnvelopeComparisonWriter`, `BaselineEnvelopeComparisonMarkdownWriter`, `Program`,
    `BaselineEnvelopeComparisonWriterTests`, `BaselineEnvelopeProgramTests`, root 상태 문서.
  - 결과: envelope JSON/Markdown artifact writer 를 추가하고,
    `--compare-baseline-envelope` CLI branch 를 reader/generator/writer 경로로 연결했다.
  - 비고: D125 기준으로 envelope signal/mismatch 는 process failure 가 아니므로 artifact 생성 성공 시 exit code 0을 유지한다.
  - Red: writer type 부재 `Assert.NotNull()` failure 2건, writer stub `NotSupportedException` failure 2건,
    Program switch 미연결 exit code 2 failure 2건을 확인했다.
  - Green: writer tests 4개, Program tests 2개, envelope 관련 tests 16개 통과.
    실제 local runner artifact CLI smoke 도 exit code 0, JSON/Markdown 생성, envelope-compatible true,
    envelope-signal-count 0으로 통과했다.
  - 검증: `git diff --check` 통과, .NET 9.0.314 MSBuild 기준 solution build 통과
    (`NU1900` vulnerability feed 조회 경고 1건), solution tests 378개 통과.
    .NET SDK 10.0.203 기본 선택 시 BenchmarkDotNet transitive package metadata `CS0006`가 재현되어
    SDK 선택 문제로 분리했다.
  - 다음: 구현 self-review 와 Phase 4 다음 후보 재평가를 수행한다.

- [x] runner/profile scoped envelope comparison Task 3 generator 를 구현했다.
  - 범위: `BaselineEnvelopeComparison*` model, `BaselineEnvelopeComparisonGenerator`,
    `BaselineEnvelopeComparisonGeneratorTests`, root 상태 문서.
  - 결과: reference history/candidate source 의 comparison key gate, eligible reference summary selection,
    kind별 metric row, D125 upper/lower limit, signal/mismatch model 을 추가했다.
  - 비고: envelope signal 은 기존 `warning-count`에 합산하지 않는 별도 model 로 남긴다.
  - Red: generator type 부재 `Assert.NotNull()` failure 1개와,
    stub generator compatible/key/signal/no-reference behavior failure 5건을 확인했다.
  - Green: `BaselineEnvelopeComparisonGeneratorTests` 6개 통과.
  - 검증: `git diff --check` 통과, solution build 경고 0/오류 0,
    solution tests 372개 통과.
  - 다음: Task 4 writer/Program wiring 을 TDD로 구현한다.

- [x] runner/profile scoped envelope comparison Task 2 source reader 를 구현했다.
  - 범위: `BaselineComparisonJsonReader`, `BaselineEnvelopeSourceKind`, `BaselineEnvelopeSummary`,
    `BaselineEnvelopeSource`, `BaselineEnvelopeSourceReader`, `BaselineHistoryReader`,
    `BaselineEnvelopeSourceReaderTests`, root 상태 문서.
  - 결과: summary/history 입력 artifact 를 같은 envelope source model 로 읽고,
    history 의 `sessions[].summary-path`를 history 파일 directory 기준으로 다시 열어 full `by-kind` aggregate 를 보존한다.
  - 비고: summary/history comparison JSON parsing 은 `BaselineComparisonJsonReader`로 공유한다.
    기존 `BaselineHistoryReader`의 legacy summary incompatible 처리 의미는 focused tests 로 유지했다.
  - Red: reader contract type 부재 `Assert.NotNull()` failure 1개와,
    stub reader `NotSupportedException` behavior failure 3건을 확인했다.
  - Green: `BaselineEnvelopeSourceReaderTests` 4개 통과, `BaselineHistoryReaderTests` 7개 통과.
  - 검증: `git diff --check` 통과, solution build 경고 0/오류 0,
    solution tests 366개 통과.
  - 다음: Task 3 generator 를 TDD로 구현한다.

- [x] runner/profile scoped envelope comparison Task 1 parser contract 를 구현했다.
  - 범위: `BenchmarkCommand`, `BenchmarkCommandLine`, `BenchmarkCommandParser`, usage text,
    `BenchmarkCommandParserTests`, root 상태 문서.
  - 결과: `--compare-baseline-envelope <candidate-json> --reference-history <reference-history-json> --envelope <output-json> [--envelope-md <output-md>]`
    parser contract 를 추가했고, candidate/reference/output path 를 `BenchmarkCommandLine`에 보존한다.
  - 비고: `--report`, `--backend`, `--protocol`은 실행 runner option 이므로 envelope comparison 과 섞이면 usage error 로 막는다.
    Program execution branch 는 Task 4 범위로 남겼다.
  - Red: compare envelope parser tests 7개가 기존 parser 에서 `parsed=false` 또는 `Command=None`으로 실패했다.
  - Green: compare envelope tests 7개 통과, 전체 `BenchmarkCommandParserTests` 29개 통과.
  - 검증: `git diff --check` 통과, solution build 경고 0/오류 0,
    solution tests 362개 통과.
  - 다음: Task 2 source reader 를 TDD로 구현한다.

- [x] runner/profile scoped envelope comparison command 구현 계획을 작성했다.
  - 범위: `docs/superpowers/plans/2026-06-29-runner-profile-envelope-comparison.md`, root 상태 문서.
  - 결과: 구현을 Task 1 parser contract, Task 2 source reader, Task 3 envelope generator,
    Task 4 writer/Program wiring 으로 분리했다.
  - 다음: Task 1 parser contract 를 TDD로 구현한다.
  - 검증: D125 spec coverage, file structure, command shape, task 경계, placeholder/type consistency self-review 를 수행했다.
    `git diff --check` 통과, solution build 경고 0/오류 0, solution tests 355개 통과.

- [x] runner/profile scoped warning envelope model 을 설계했다.
  - 범위: `docs/superpowers/specs/2026-06-29-runner-profile-warning-envelope-model-design.md`,
    `DECISIONS.md`, `docs/agent-state/decisions/2026-06.md`, `docs/benchmarks/baselines/index.md`,
    root 상태 문서.
  - 결과: D125로 기존 `warning-count`와 `BaselineSummaryGenerator` 전역 threshold 는 유지하고,
    runner/profile/workload scoped 판단은 별도 envelope comparison artifact 로 분리하기로 했다.
  - 비고: reference envelope 는 `history.json`이 가리키는 session `summary.json`들을 재사용해 계산한다.
    envelope signal 은 `envelope-signal-count`로 기록하고 process failure, CI failure, warning-as-failure 로 승격하지 않는다.
  - 다음: envelope comparison command 구현 계획을 작성한다.
  - 검증: 기존 summary/history schema, D080/D090/D096/D123/D124 결정, baseline index 를 대조했다.
    placeholder scan 매칭 없음, `git diff --check` 통과, solution build 경고 0/오류 0,
    solution tests 355개 통과.

- [x] Phase 4 explicit runner 3-date-root evidence 기반 warning/gate promotion policy 를 재평가했다.
  - 범위: `docs/superpowers/specs/2026-06-29-phase4-gate-promotion-policy-after-local-3-date-roots.md`,
    `docs/benchmarks/baselines/index.md`, `DECISIONS.md`, `docs/agent-state/decisions/2026-06.md`,
    root 상태 문서.
  - 결과: D124로 `local-win-x64-01` 9-session envelope 를 runner-local reference envelope 로 채택했다.
    이 envelope 는 후속 local baseline 수동 리뷰 기준이며, CLI exit code, CI failure, process failure 로 자동 승격하지 않는다.
  - 비고: 현재 `BaselineSummaryGenerator` warning threshold 는 runner/profile scoped 가 아닌 전역 상수다.
    local SAEA TCP loopback 수치를 전역 threshold 로 낮추면 CI/RIO/UDP benchmark 에도 같은 기준이 적용되므로,
    기존 soft warning threshold 는 그대로 둔다.
  - 다음: runner/profile scoped warning envelope model 을 별도 설계한다.
  - 검증: local/CI runner history 와 warning threshold 구조를 대조했다.
    `git diff --check` 통과, solution build 경고 0/오류 0, solution tests 355개 통과.

- [x] `local-win-x64-01/2026-06-29` explicit runner baseline 3-session 을 수집했다.
  - 범위: `docs/benchmarks/baselines/runners/local-win-x64-01/2026-06-29/`,
    `docs/benchmarks/baselines/runners/local-win-x64-01/history.json`,
    `docs/benchmarks/baselines/runners/local-win-x64-01/history.md`,
    `docs/benchmarks/baselines/index.md`, root 상태/결정 문서.
  - 결과: 2026-06-29 date root 는 session-count 3, hard-passed true, warning-count 0,
    comparison-compatible true 다. runner root history 는 총 9-session 을 묶고 같은 상태를 유지한다.
  - 대표값: session별 load p99 max 는 844.6/856.7/884.6 us,
    open-loop p99 max 는 948.8/878.3/978.9 us, TCP HWM max 는 모두 2다.
    9-session explicit runner envelope 는 load p99 max 935.6 us, open-loop p99 max 1077.4 us 로 기존 maxima 를 유지한다.
  - 결정: D123으로 D082 explicit runner 3-date-root evidence 조건 충족을 기록하되,
    warning-as-failure/CI latency gate 승격은 별도 정책 재평가로 분리했다.
  - 검증: `--baseline-suite` 3회, `--summarize-baseline` 3회, date-level/runner-level `--summarize-baseline-history`를 실행했다.
    새 baseline artifact 절대 경로 검색 매칭 없음, `git diff --check` 통과, solution build 경고 0/오류 0,
    solution tests 355개 통과.

- [x] RIO address-family-aware host selection 정책과 TCP IPv6 guard 를 구현했다.
  - 범위: `src/Hps.Transport.Rio/RioTransport.cs`,
    `samples/Hps.Sample.BrokerServer/Program.cs`,
    `samples/Hps.Sample.BrokerServer/SampleTransportSelector.cs`,
    `tests/Hps.Transport.Rio.Tests/RioTransportTcpTests.cs`,
    `tests/Hps.Sample.BrokerServer.Tests/SampleTransportSelectorTests.cs`,
    `docs/superpowers/specs/2026-06-29-rio-address-family-aware-selection-policy-design.md`,
    `docs/superpowers/plans/2026-06-29-rio-address-family-aware-selection.md`.
  - 결과: D122로 RIO backend 현재 지원 범위를 TCP/UDP IPv4-only opt-in 으로 명시했다.
    RIO TCP listen/connect 는 IPv6 endpoint 를 socket 계층 전에 `NotSupportedException`으로 거부한다.
    sample broker `auto`는 IPv6/non-IPv4 listen endpoint 에서 SAEA fallback notice 를 반환하고,
    explicit `rio`는 runtime failure 를 반환한다.
  - Red: RIO TCP IPv6 listen 은 기존 구현에서 `SocketException`으로 실패했고, connect 는 `IPv4` 없는 메시지로 실패했다.
    selector IPv6 tests 는 새 address-family-aware overload 부재 `Assert.NotNull()` failure 로 실패했다.
  - Green/검증: focused guard tests 2개 통과, focused selector tests 2개 통과,
    `Hps.Transport.Rio.Tests` 57개 통과, `Hps.Sample.BrokerServer.Tests` 17개 통과,
    solution build 경고 0/오류 0, solution tests 355개 통과, `git diff --check` 통과.

- [x] RIO UDP IPv6 unsupported boundary guard 를 TDD로 구현했다.
  - 범위: `src/Hps.Transport.Rio/RioTransport.cs`,
    `tests/Hps.Transport.Rio.Tests/RioTransportUdpTests.cs`,
    `docs/superpowers/plans/2026-06-26-rio-udp-ipv6-unsupported-guard.md`, root 상태 문서.
  - 결과: IPv6 local bind 는 명시적 `NotSupportedException`으로 거부하고,
    IPv6 remote send 는 pending queue 에 넣지 않고 `false`를 반환한다.
  - Red: 기존 구현에서 IPv6 bind 는 `SocketException`으로 실패했고, IPv6 remote `TrySendTo`는 `true`를 반환했다.
  - Green/검증: focused guard tests 2개 통과, `Hps.Transport.Rio.Tests` 55개 통과,
    solution build 경고 0/오류 0, solution tests 351개 통과, `git diff --check` 통과.

- [x] RIO UDP IPv6 support gate 설계와 unsupported guard 구현 계획을 작성했다.
  - 범위: `docs/superpowers/specs/2026-06-26-rio-udp-ipv6-support-gate-design.md`,
    `docs/superpowers/plans/2026-06-26-rio-udp-ipv6-unsupported-guard.md`,
    `DECISIONS.md`, `docs/agent-state/decisions/2026-06.md`, root 상태 문서.
  - 결과: D121로 RIO UDP v1을 IPv4-only opt-in backend 로 유지하고,
    IPv6는 default promotion gate 로 남긴다고 결정했다.
    다음 구현은 full IPv6가 아니라 unsupported local/remote endpoint boundary guard 다.
  - 검증: RIO/SAEA UDP address-family source 대조, D109/D110/D118/D119 결정 대조,
    설계/계획 placeholder self-review 를 수행했다.

- [x] sample broker transport selector 구현 self-review 를 완료하고 minor hardening 2건을 보정했다.
  - 범위: `samples/Hps.Sample.BrokerServer/`, `tests/Hps.Sample.BrokerServer.Tests/`,
    `docs/superpowers/specs/2026-06-26-host-composition-transport-selection-policy-design.md`,
    `docs/superpowers/plans/2026-06-26-sample-broker-transport-selector.md`,
    `docs/agent-state/reviews/2026-06-26-sample-broker-transport-selector-self-review.md`.
  - 결과: D120 구현은 설계와 정합하고 Blocker/Major finding 은 없다.
    parser invalid port/max-frame 메시지 누락과 undefined enum fallback 오해 가능성은 TDD로 보정했다.
  - Red: invalid port/max-frame parser tests 2개가 `Assert.Equal()` failure 로 실패했고,
    undefined enum selector test 1개가 `Assert.Throws()` failure 로 실패했다.
  - Green/검증: focused parser/selector tests 13개 통과, sample broker server tests 15개 통과,
    solution build 경고 0/오류 0, solution tests 349개 통과, `git diff --check` 통과.

- [x] sample broker server transport selector Task 3 Program wiring/smoke 를 구현했다.
  - 범위: `samples/Hps.Sample.BrokerServer/Program.cs`,
    `tests/Hps.Sample.BrokerServer.Tests/SampleBrokerServerProgramTests.cs`,
    implementation plan/state docs.
  - 결과: Program 이 parser/selector 로 transport 를 생성하고 startup output 에 selected backend 를 표시한다.
    usage 는 `[--transport <saea|rio|auto>]`를 포함한다.
  - Red: Program usage tests 2개가 기존 usage output 에 `--transport <saea|rio|auto>`가 없어 `Assert.Contains()` failure 로 실패했다.
  - Green/검증: focused Program tests 2개 통과, focused sample tests 12개 통과,
    solution build 경고 0/오류 0, solution tests 346개 통과, `git diff --check` 통과.

- [x] sample broker server transport selector Task 2 selection policy 를 구현했다.
  - 범위: `samples/Hps.Sample.BrokerServer/Hps.Sample.BrokerServer.csproj`,
    `SampleTransportSelection.cs`, `SampleTransportSelector.cs`,
    `tests/Hps.Sample.BrokerServer.Tests/SampleTransportSelectorTests.cs`.
  - 결과: `saea`는 SAEA, explicit `rio`는 available 일 때 RIO/unavailable 일 때 failure,
    `auto`는 available 일 때 RIO/unavailable 또는 unsupported 일 때 SAEA fallback notice 를 반환한다.
  - Red: 최초 selector tests 는 RIO test reference 누락 컴파일 오류를 보정한 뒤,
    selector type 부재 `Assert.NotNull()` failure 로 실패했다.
  - Green/검증: focused selector tests 5개 통과, focused sample tests 10개 통과,
    solution build 경고 0/오류 0, solution tests 344개 통과, `git diff --check` 통과.

- [x] sample broker server transport selector Task 1 parser/model 을 구현했다.
  - 범위: `HighPerformanceSocket.slnx`, `tests/Hps.Sample.BrokerServer.Tests/`,
    `samples/Hps.Sample.BrokerServer/SampleTransportMode.cs`,
    `SampleBrokerServerCommandLine.cs`, `SampleBrokerServerCommandParser.cs`.
  - 결과: 기존 3 positional args 는 SAEA mode 로 해석되고,
    optional `--transport rio|auto`는 parser model 에 보존된다.
    값 누락과 unknown value 는 broker start 전 usage error 로 반환된다.
  - Red: focused parser tests 5개가 parser type 부재 `Assert.NotNull()` failure 로 실패했다.
  - Green/검증: focused parser tests 5개 통과, solution build 경고 0/오류 0,
    solution tests 339개 통과, `git diff --check` 통과.

- [x] sample broker server transport selector 구현 계획을 작성했다.
  - 범위: `docs/superpowers/plans/2026-06-26-sample-broker-transport-selector.md`, D120 설계 문서, sample/benchmark 구조.
  - 결과: 구현을 Task 1 parser/model, Task 2 selector policy, Task 3 Program wiring/smoke 로 나눴다.
  - 비고: Task 1은 parser contract 만 닫고, RIO capability/fallback policy 와 Program wiring 은 후속 단위로 분리한다.
  - 검증: spec coverage, placeholder scan, C# 접근성/async helper 제약 self-review 를 수행했다.

- [x] host/composition transport selection policy 설계를 완료했다.
  - 범위: `samples/Hps.Sample.BrokerServer`, `tests/Hps.Benchmarks` backend selector 선례,
    `src/Hps.Server`, `src/Hps.Transport.Rio/RioCapabilityProbe.cs`, D119 decision.
  - 결과: D120으로 첫 적용 대상을 sample broker server 로 정하고 optional `--transport <saea|rio|auto>` 정책을 기록했다.
  - 비고: 기본값은 `saea`, explicit `rio`는 unavailable 시 실패, `auto`는 RIO unavailable/unsupported 시 관측 가능한 SAEA fallback 이다.
  - 검증: current sample host, benchmark explicit selector, BrokerServer injected transport 경계를 대조했다.

- [x] RIO UDP gate 이후 default selection policy 설계를 완료했다.
  - 범위: `src/Hps.Transport/Runtime/TransportFactory.cs`, `src/Hps.Transport.Rio/RioCapabilityProbe.cs`,
    D108/D110/D118 decisions, benchmark explicit RIO path, root 상태 문서.
  - 결과: D119로 `TransportFactory.CreateDefault()`는 계속 deterministic SAEA default 를 반환하고,
    RIO preferred fallback 정책은 host/composition layer 또는 별도 selector package 에 둔다고 기록했다.
  - 비고: base factory 직접 RIO 참조와 reflection 기반 RIO loading 은 의존 방향/배포/관측성 문제로 채택하지 않는다.
  - 검증: current factory behavior, RIO capability probe, explicit benchmark backend selector, D118 scratch evidence 를 대조했다.

- [x] RIO UDP bounded receive window Task 1 depth-2 receive behavior 를 구현했다.
  - 범위: `src/Hps.Transport.Rio/RioTransport.cs`, `src/Hps.Transport.Rio/RioUdpEndpoint.cs`,
    `tests/Hps.Transport.Rio.Tests/RioTransportUdpTests.cs`, 구현 계획 문서, root 상태 문서.
  - 결과: `MaxOutstandingReceive`를 2로 올리고, UDP receive loop 를 `RioResult.RequestContext` 기반 slot window 로 전환했다.
    receive remote address 는 slot-local registered buffer 로 이동했고, payload data buffer 는 D113대로 completion 직후 deregister 한다.
  - Red: `UdpReceive_WhenHandlerIsBlocked_PreservesTwoQueuedDatagramsWithBoundedWindow`가 기존 one-deep 구현에서
    `Expected: 3`, `Actual: 2`로 실패했다.
  - Green/검증: focused Red test 1개 통과, `RioTransportUdpTests` 16개 통과, `Hps.Transport.Rio.Tests` 53개 통과.

- [x] RIO UDP bounded receive window Task 2 close/drain cleanup hardening 을 확인했다.
  - 범위: bounded receive window 구현 계획, existing close/handler-exception cleanup tests, root 상태 문서.
  - 결과: Task 1의 slot cleanup 구현이 data registration, slot-local remote address registration,
    outstanding datagram ref 를 모두 정리하며, receive loop finally 가 slot 배열 dispose 후 endpoint receive CQ를 닫는다.
  - 검증: focused cleanup tests 2개 통과.
  - 비고: 별도 production 변경이나 별도 커밋 없이 Task 1 commit `0a03a17`의 구현으로 닫았다.

- [x] RIO UDP bounded receive window Task 3 scratch benchmark 와 D118 판단을 완료했다.
  - 범위: ignored scratch `artifacts/benchmarks/rio-udp/2026-06-26/session-04/rio/`,
    `DECISIONS.md`, `docs/agent-state/decisions/2026-06.md`, bounded receive window 구현 계획, root 상태 문서.
  - 결과: D118을 accepted 로 기록했다. RIO `session-04/load`는 sent/received 3000/3000, p99 831.8 us,
    RIO `session-04/open-loop`은 sent/received 3000/3000, p99 889.4 us 로 hard-passed true/warning 0 이다.
  - 검증: baseline suite exit 0, summary exit 0, old RIO session-03/session-02 및 SAEA session-01 비교.
  - 비고: scratch artifact 는 `artifacts/` ignore 정책에 따라 stage 하지 않는다.

- [x] RIO UDP open-loop delivery loss 의 receive-side 설계와 구현 계획을 작성했다.
  - 범위: `docs/superpowers/specs/2026-06-26-rio-udp-bounded-receive-window-design.md`,
    `docs/superpowers/plans/2026-06-26-rio-udp-bounded-receive-window.md`, decisions/root 상태 문서.
  - 결과: D117로 다음 구현 후보를 receive payload registration reuse 가 아니라 bounded receive slot window 로 결정했다.
    첫 depth 는 2, mapping 은 `RioResult.RequestContext`, remote address 는 slot-local, payload data buffer 는 D113대로 completion 직후 deregister 한다.
  - 비고: implementation 은 Task 1 behavior, Task 2 close/drain cleanup, Task 3 benchmark/D118 판단으로 나눴다.

- [x] RIO UDP completion notification wait Task 3 scratch benchmark 와 D116 판단을 완료했다.
  - 범위: scratch artifact `artifacts/benchmarks/rio-udp/2026-06-26/session-03/rio/`,
    `DECISIONS.md`, `docs/agent-state/decisions/2026-06.md`, root 상태 문서, 구현 계획 문서.
  - 결과: D116을 partial 로 기록했다. RIO UDP p99 wake tail 은 16.7ms대에서 load 481 us, open-loop 647.6 us 로 개선됐지만,
    open-loop delivery 는 sent/received 3000/2373 으로 hard gate 실패가 남았다.
  - 검증: raw report 2개와 summary artifact 생성, old RIO session-02 및 SAEA session-01 과 비교.
  - 비고: scratch artifact 는 `artifacts/` ignore 정책에 따라 stage 하지 않는다.

- [x] RIO UDP completion notification wait Task 2 wait path 를 구현했다.
  - 범위: `src/Hps.Transport.Rio/RioUdpEndpoint.cs`, `src/Hps.Transport.Rio/RioTransport.cs`,
    `tests/Hps.Transport.Rio.Tests/RioTransportUdpTests.cs`, 구현 계획 문서, root 상태 문서.
  - 결과: `RioUdpEndpoint.ArmNotification(...)`이 CQ drain 과 같은 lock 에서 `RIONotify`를 arm 하고,
    `WaitForUdpCompletionAsync(...)`는 open 상태에서 `Task.Delay(1)` polling 없이 signal wait 로 대기한다.
    close-drain fallback 은 owner cleanup 을 위해 제한적으로 유지한다.
  - Red: `RioUdpEndpoint_WhenNotificationWaitIsExpected_ExposesArmNotificationHelper`가 기존 endpoint 에서
    `Assert.NotNull()` failure 로 실패했다.
  - 검증: Red/Green focused test, `RioTransportUdpTests` 15개, `Hps.Transport.Rio.Tests` 52개 통과.

- [x] RIO UDP completion notification wait Task 1 endpoint signal shape 를 구현했다.
  - 범위: `src/Hps.Transport.Rio/RioUdpEndpoint.cs`, `src/Hps.Transport.Rio/RioTransport.cs`,
    `tests/Hps.Transport.Rio.Tests/RioTransportUdpTests.cs`, 구현 계획 문서, root 상태 문서.
  - 결과: `RioUdpEndpoint`가 receive/send `RioCompletionSignal`을 소유하고,
    UDP receive/send CQ를 notification completion pointer 로 생성한다.
    `RioTransport.BindUdpAsync(...)`는 shared `RioCompletionPort`를 endpoint 에 넘긴다.
  - Red: `BindUdpAsync_WhenRioDatagramAvailable_CreatesUdpCompletionSignals`가 기존 endpoint 에서
    `Assert.NotNull()` failure 로 실패했다.
  - Green/검증: focused Red test 1개 통과, focused `RioTransportUdpTests` 14개 통과,
    focused `Hps.Transport.Rio.Tests` 51개 통과.

- [x] RIO UDP IOCP/RIONotify completion wait 구현 계획을 작성했다.
  - 범위: `docs/superpowers/plans/2026-06-26-rio-udp-completion-notification-wait.md`, root 상태 문서.
  - 결과: D115 설계를 endpoint signal resource shape, UDP wait notification 전환, scratch benchmark/D116 판단의
    3개 작업 단위로 나눴다.
  - 검증: D115 설계 coverage, TCP RIO completion wait working pattern, Red assertion-failure 경로,
    commit boundary 를 self-review 했다.

- [x] RIO UDP open-loop residual loss/tail 재평가 설계를 작성했다.
  - 범위: `docs/superpowers/specs/2026-06-26-rio-udp-open-loop-residual-loss-tail-design.md`,
    `DECISIONS.md`, `docs/agent-state/decisions/2026-06.md`, root 상태 문서.
  - 결과: D115로 다음 구현 후보를 UDP CQ IOCP/RIONotify wait parity 로 결정했다.
    receive depth 확대, receive payload registration reuse, latency hard gate 승격은 이번 다음 구현 범위에서 제외한다.
  - 근거: RIO UDP p99 16.7ms tail 은 현재 `WaitForUdpCompletionAsync(...)`의 `Task.Delay(1)` fallback 및 Windows timer quantum 과 맞고,
    SAEA UDP는 같은 harness 에서 p99 0.85ms 수준으로 통과했다.
  - 다음: endpoint receive/send signal resource shape, UDP wait notification 전환, scratch benchmark 재측정을 구현 계획으로 나눈다.

- [x] RIO UDP receive window hardening Task 2 benchmark 재수집과 D114 문서화를 완료했다.
  - 범위: `DECISIONS.md`, `docs/agent-state/decisions/2026-06.md`, `CURRENT_PLAN.md`,
    `TODOS.md`, `CHANGELOG_AGENT.md`, ignored scratch `artifacts/benchmarks/rio-udp/2026-06-26/session-02/`.
  - 결과: D114로 close-safe one-deep pre-post receive policy 를 수락하고 D111 no-prefetch receive window 정책을 supersede 했다.
  - benchmark: `--baseline-suite artifacts\benchmarks\rio-udp\2026-06-26\session-02\rio --runs 1 --protocol udp --backend rio`
    는 raw report 2개를 생성했지만 open-loop hard gate 실패로 exit code 1을 반환했다.
  - evidence: load 는 sent/received 3000/3000, dropped 0, pool-rented 0, actual-rate 99.7 Hz, p99 16719.2 us,
    passed true. open-loop 는 sent 3000 / received 2409, dropped 0, payload-errors 0, pool-rented 0,
    actual-rate 85.7 Hz, p99 16709.1 us, passed false.
  - summary: `summary.json`/`summary.md` 생성 결과 hard-passed false, warning-count 3
    (`load-p99-latency-high`, `open-loop-p99-latency-high`, `actual-rate-low`)이다.
  - 비고: one-deep pre-post 는 수명/소유권 정책으로 수락하지만, RIO UDP open-loop 목표 달성은 다음 residual loss/tail 분석 전에는 주장하지 않는다.

- [x] RIO UDP receive window hardening Task 1 close-safe one-deep receive loop 를 구현했다.
  - 범위: `src/Hps.Transport.Rio/RioTransport.cs`, `src/Hps.Transport.Rio/RioUdpEndpoint.cs`,
    `tests/Hps.Transport.Rio.Tests/RioTransportUdpTests.cs`, root 상태 문서.
  - 결과: `RioUdpReceiveOperation` owner 를 추가하고 handler dispatch 전에 다음 receive 를 하나 pre-post 한다.
    `RioUdpEndpoint.Close()`는 shutdown request 로 제한하고 receive/send native resource 는 각 pump drain 이후 정리한다.
  - Red: one-deep receive/close 테스트 2개가 기존 no-prefetch 구현에서 `Expected: 2, Actual: 1`로 실패했다.
  - Green/검증: focused one-deep tests 3개 통과, focused `RioTransportUdpTests` 13개 통과,
    focused `Hps.Transport.Rio.Tests` 50개 통과, solution build 경고 0/오류 0,
    solution tests 331개 통과.

- [x] RIO UDP receive window hardening 구현 계획을 작성했다.
  - 범위: `docs/superpowers/plans/2026-06-26-rio-udp-receive-window-hardening.md`, root 상태 문서.
  - 결과: 리뷰 반영된 one-deep pre-post 설계를 Task 1 close-safe receive loop 구현과
    Task 2 benchmark/D114 문서화로 나눴다.
    Task 1은 close-drain blocker 때문에 receive operation owner 와 endpoint resource split 을 같은 구현 단위로 묶는다.
  - 검증: 설계 리뷰 B1~B5, D111/D113, scratch evidence, RIO UDP test helper 구조와 계획을 대조했다.
    placeholder scan 과 `git diff --check`로 문서 변경을 검증했다.

- [x] RIO UDP receive window hardening 설계 초안을 작성했다.
  - 범위: `docs/superpowers/specs/2026-06-26-rio-udp-receive-window-hardening-design.md`, root 상태 문서.
  - 결과: no-prefetch 유지, one-deep pre-post, bounded outstanding receive queue 를 비교했고,
    첫 구현 후보는 handler 병렬 호출 없이 다음 receive 를 dispatch 전에 post 하는 one-deep pre-post 로 정리했다.
  - 검증: placeholder scan 통과, D111/D113과 scratch benchmark evidence 대조.

- [x] RIO/SAEA UDP benchmark scratch artifact 를 수집하고 RIO UDP receive/fan-out 경계 버그를 보정했다.
  - 범위: `src/Hps.Transport.Rio/RioTransport.cs`, `src/Hps.Transport.Rio/RioUdpEndpoint.cs`,
    `tests/Hps.Transport.Rio.Tests/RioTransportUdpTests.cs`,
    `tests/Hps.Benchmarks/UdpLoopbackScenarioRunner.cs`,
    `tests/Hps.Benchmarks.Tests/UdpLoopbackScenarioRunnerTests.cs`, state/decision docs.
  - 결과: RIO UDP receive completion 뒤 handler dispatch 전에 native receive registration 을 해제하고,
    UDP receive block 을 SAEA 기준선과 같은 8192B 로 올렸다(D113).
    benchmark runner 는 closed-loop timeout 도 failed raw report 로 남기고,
    open-loop sequence gap 을 payload corruption 과 분리한다.
  - artifact: ignored scratch `artifacts/benchmarks/rio-udp/2026-06-26/session-01/`에 SAEA/RIO backend별 raw report 와 summary 를 생성했다.
    SAEA summary 는 hard-passed true/warning 0.
    RIO summary 는 open-loop sent 3000 / received 2263 / payload-errors 0 으로 hard-passed false/warning 3.
  - Red: RIO UDP smoke/baseline-suite timeout, two-remote fan-out timeout,
    4224B datagram endpoint close, sequence gap payload pattern assertion failure.
  - Green/검증: focused RIO UDP fan-out/large datagram tests 통과, focused benchmark payload gap tests 통과,
    RIO UDP smoke pass, SAEA/RIO scratch summary 생성.

- [x] RIO UDP benchmark load/open-loop/baseline-suite 를 구현했다.
  - 범위: `tests/Hps.Benchmarks/UdpLoopbackScenarioRunner.cs`, `tests/Hps.Benchmarks/Program.cs`,
    `tests/Hps.Benchmarks.Tests/UdpLoopbackScenarioRunnerTests.cs`, root 상태 문서.
  - 결과: UDP runner 가 smoke/load/open-loop 를 하나의 scenario core 로 실행하고,
    baseline-suite 는 `--protocol udp` 선택값에 따라 UDP load/open-loop raw report 를 반복 생성한다.
    closed-loop 는 publish 뒤 receive 를 기다리고, open-loop 는 receive task 와 publish schedule 을 분리해
    delivery/drop/leak hard gate 를 report 로 남긴다.
  - Red: focused `UdpLoopbackScenarioRunnerTests` 2개가 기존 private test entry point 부재로 `Assert.NotNull()` 실패했다.
  - Green/검증: focused UDP runner tests 2개 통과, `Hps.Benchmarks.Tests` 78개 통과,
    실제 SAEA UDP load/open-loop CLI pass, 실제 SAEA UDP baseline-suite 1-run pass,
    solution build 경고 0/오류 0, solution tests 325개 통과.

- [x] RIO UDP benchmark artifact 수집 범위와 command shape 를 설계했다.
  - 범위: `docs/superpowers/specs/2026-06-26-rio-udp-benchmark-artifact-design.md`,
    D112 결정 문서, root 상태 문서.
  - 결과: 기존 benchmark 명령에 `--protocol <tcp|udp>` selector 를 추가하고,
    UDP report 는 기존 raw report schema 를 재사용하며 `benchmark-profile`/`scenario`로 TCP/UDP를 구분하기로 했다.
  - 비고: 첫 RIO UDP evidence 는 repository baseline 이 아니라 `artifacts/benchmarks/rio-udp/...` scratch 영역에 수집한다.
  - 검증: benchmark CLI/result/schema source 대조, 설계 문서 placeholder scan, `git diff --check`, solution build/test.

- [x] RIO UDP benchmark Task 1 protocol selector model/parser 를 구현했다.
  - 범위: `tests/Hps.Benchmarks/BenchmarkCommandLine.cs`, `BenchmarkCommandParser.cs`, `LoopbackProtocol.cs`,
    `Program.cs`, `tests/Hps.Benchmarks.Tests/BenchmarkCommandParserTests.cs`, `BenchmarkProgramProtocolTests.cs`.
  - 결과: runner/baseline-suite command 가 `--protocol <tcp|udp>`를 파싱해 `BenchmarkCommandLine.LoopbackProtocol`에 보존한다.
    summary/history/help/target 또는 runner 없는 위치에서는 `--protocol`을 usage error 로 막는다.
    UDP runner 연결 전까지는 Program guard 가 `--protocol udp` 실행을 실패 처리해 TCP artifact 오생성을 막는다.
  - Red: parser focused run 에서 4개 테스트가 `알 수 없는 benchmark runner 인자입니다.` 또는 protocol 없는 usage error 로 실패했다.
    Program guard Red 는 `--smoke --protocol udp --report ...`가 exit code 0을 반환해 실패했다.
  - Green/검증: parser focused 22개 통과, Program guard focused 1개 통과,
    `Hps.Benchmarks.Tests` 76개 통과, solution build 0경고/0오류, solution tests 323개 통과.

- [x] RIO UDP benchmark Task 2/3 UDP loopback runner dispatch 와 SAEA UDP smoke 를 구현했다.
  - 범위: `tests/Hps.Benchmarks/UdpLoopbackScenarioRunner.cs`, `Program.cs`, `BenchmarkRunIdentity.cs`,
    `tests/Hps.Benchmarks.Tests/BenchmarkProgramProtocolTests.cs`, root 상태 문서.
  - 결과: `--smoke --protocol udp --backend saea --report <path>`가 `BrokerServer.StartUdpAsync(...)`와
    UDP `SUBSCRIBE`/`PUBLISH` datagram loopback 을 실행해 `udp-loopback-saea-baseline-smoke` raw report 를 만든다.
    report schema 는 기존 writer 를 재사용하며 `benchmark-profile=udp-loopback-saea-v1`을 기록한다.
  - Red: Program test 가 기존 guard 때문에 exit code 1로 실패했다.
  - Green/검증: focused Program protocol test 1개 통과, `Hps.Benchmarks.Tests` 76개 통과,
    실제 SAEA UDP smoke CLI pass, solution build 0경고/0오류, solution tests 323개 통과.

- [x] RIO/SAEA backend contract matrix 를 RIO UDP edge tests 로 보강했다.
  - 범위: `tests/Hps.Transport.Rio.Tests/RioTransportUdpTests.cs`, D111 결정 문서, root 상태 문서.
  - 결과: RIO UDP handler exception close notify, no-prefetch/pool ownership, endpoint close-drain,
    drop-oldest release/diagnostics/high-watermark 를 테스트로 고정했다.
  - Red: 최초 no-prefetch 테스트는 blocked handler 중 보낸 두 번째 datagram 을 unblock 뒤 보장 수신한다고 기대해 timeout 으로 실패했다.
    D111 기준으로 RIO no-prefetch 는 blocked-window datagram retention 이 아니라 pool prefetch 없음과 loop 생존을 검증하도록 보정했다.
  - Green/검증: focused `RioTransportUdpTests` 8개 통과, focused RIO tests 45개 통과,
    solution build 0경고/0오류, solution tests 318개 통과.
  - 비고: bounded receive prefetch 는 benchmark evidence 이후 별도 설계 후보로 deferred 했다.

- [x] RIO UDP parity/default promotion readiness 를 재검토했다.
  - 범위: `src/Hps.Transport.Rio/`, `src/Hps.Transport/Runtime/TransportFactory.cs`,
    RIO/SAEA transport tests, D108/D109/D110 결정 문서, root 상태 문서.
  - 결과: RIO UDP native Ex, endpoint owner, receive loop, send loop, diagnostics parity 는 완료됐지만,
    IPv4-only UDP, 공유 contract matrix 부족, fallback policy 부재, UDP benchmark evidence 부족 때문에
    `TransportFactory.CreateDefault()`는 계속 `SaeaTransport`를 반환한다고 정리했다(D110).
  - 검증: source/test/decision matrix 대조, review 문서 작성, `git diff --check`,
    solution build 0경고/0오류, solution tests 314개 통과.
  - 비고: 다음 작업은 default promotion 이 아니라 RIO/SAEA backend contract matrix 보강이다.

- [x] RIO UDP Task 5 diagnostics parity 를 구현했다.
  - 범위: `src/Hps.Transport.Rio/RioTransport.cs`, `src/Hps.Transport.Rio/RioUdpEndpoint.cs`,
    `tests/Hps.Transport.Rio.Tests/RioTransportUdpTests.cs`, root 상태 문서.
  - 결과: `RioTransport`가 `ITransportEndpointDiagnostics`를 구현하고,
    RIO UDP endpoint snapshot 이 SAEA UDP와 같은 state/send queue/drop 관측값을 제공한다.
  - Red: `GetEndpointSnapshots_WhenUdpEndpointIsOpen_ReturnsUdpSnapshot`가
    `ITransportEndpointDiagnostics` assignability failure 로 실패.
  - Green/검증: focused diagnostics test 통과, focused RIO tests 41개 통과,
    solution build 0경고/0오류, solution tests 314개 통과.
  - 비고: default backend promotion 은 별도 readiness 재평가 후 결정한다.

- [x] RIO UDP Task 4 send loop 를 구현했다.
  - 범위: `src/Hps.Transport.Rio/RioTransport.cs`, `src/Hps.Transport.Rio/RioUdpEndpoint.cs`,
    `tests/Hps.Transport.Rio.Tests/RioTransportUdpTests.cs`, root 상태 문서.
  - 결과: RIO `TrySendTo(...)`, endpoint-local bounded pending queue/drop-oldest,
    payload registration cache lease, send remote address registered buffer, `RIOSendEx` send pump 를 연결했다.
  - Red: `UdpEcho_WhenDatagramHandlerQueuesResponse_ClientReceivesSamePayload`가 client receive timeout 으로 실패.
  - Green/검증: focused UDP echo test 통과, focused RIO tests 40개 통과,
    solution build 0경고/0오류, solution tests 313개 통과.
  - 비고: endpoint snapshot diagnostics parity 는 다음 task 로 남긴다.

- [x] RIO UDP Task 3 receive loop 를 구현했다.
  - 범위: `src/Hps.Transport.Rio/RioTransport.cs`, `src/Hps.Transport.Rio/RioUdpEndpoint.cs`,
    `tests/Hps.Transport.Rio.Tests/RioTransportUdpTests.cs`, `tests/Hps.Transport.Rio.Tests/Properties/AssemblyInfo.cs`,
    root 상태 문서.
  - 결과: `RIOReceiveEx` post/completion/decode/dispatch 경로를 추가하고,
    `RioUdpEndpoint`가 UDP RQ/CQ, remote address registered buffer, receive pool 을 소유한다.
    첫 receive post 는 `BindUdpAsync(...)` 반환 전에 수행하고, UDP v1 completion wait 는 bounded dequeue polling 으로 둔다.
  - Red: `UdpReceive_WhenRawClientSendsDatagram_DeliversOwnedRefCountedBuffer`가 기존 skeleton 에서 handler timeout 으로 실패.
  - Green/검증: focused UDP receive test 통과, focused RIO tests 39개 통과,
    solution build 0경고/0오류, solution tests 312개 통과.
  - 비고: RIO native integration tests 는 provider/CQ 자원 공유 때문에 test project collection parallelization 을 비활성화했다.

- [x] RIO UDP Task 2 endpoint owner skeleton 을 구현했다.
  - 범위: `src/Hps.Transport.Rio/RioNative.cs`, `src/Hps.Transport.Rio/RioTransport.cs`,
    `src/Hps.Transport.Rio/RioUdpEndpoint.cs`, `tests/Hps.Transport.Rio.Tests/RioTransportUdpTests.cs`,
    root 상태 문서.
  - 결과: registered UDP socket 생성 helper, RIO datagram capability 확인, bind endpoint 생성,
    close/unregister owner 를 추가했다.
  - Red: `BindUdpAsync_WhenRioDatagramAvailable_ReturnsEndpointWithLocalEndPoint`가
    `TransportBase.BindUdpAsync`의 `NotImplementedException`으로 실패.
  - Green/검증: focused UDP test 1개 통과, focused RIO tests 38개 통과,
    solution build 0경고/0오류, solution tests 통과.
  - 비고: receive/send loop, pending queue, diagnostics parity 는 후속 task 다.

- [x] RIO UDP Task 1 native Ex operation shape 를 구현했다.
  - 범위: `src/Hps.Transport.Rio/RioNative.cs`, `tests/Hps.Transport.Rio.Tests/RioCapabilityProbeTests.cs`,
    root 상태 문서.
  - 결과: `SupportsDatagramOperations`, `ReceiveEx`, `SendEx`, optional `RioBufferSegment` pinning helper 를 추가했다.
  - Red: focused tests 2개가 property/method 부재로 `Assert.NotNull()` 실패.
  - Green/검증: focused Ex tests 3개 통과, focused RIO tests 37개 통과,
    solution build 0경고/0오류, solution tests 통과.
  - 비고: UDP endpoint, sockaddr encode/decode, live datagram loopback 은 Task 2 이후 범위다.

- [x] RIO UDP Task 1 native Ex operation shape 구현 계획을 작성했다.
  - 범위: `docs/superpowers/plans/2026-06-25-rio-udp-native-ex-operation-shape.md`, root 상태 문서.
  - 결과: `ReceiveEx`/`SendEx` delegate binding, `SupportsDatagramOperations`,
    nullable `RIO_BUF` marshalling, capability/argument validation tests 로 Task 1 범위를 제한했다.
  - 검증: D109 coverage self-review, placeholder scan, `git diff --check`.
  - 비고: 다음 실행은 Red tests 작성이다.

- [x] RIO UDP backend boundary 설계를 완료했다.
  - 범위: `docs/superpowers/specs/2026-06-25-rio-udp-backend-boundary-design.md`,
    `DECISIONS.md`, `docs/agent-state/decisions/2026-06.md`, root 상태 문서.
  - 결과: D109로 RIO UDP는 TCP resource 를 재사용하지 않고 UDP endpoint owner 로 설계한다.
    구현 순서는 native Ex wrapper, endpoint skeleton, receive loop, send loop, diagnostics parity 로 잡았다.
  - 검증: SAEA UDP endpoint/handler 계약, RIO native function table shape, Microsoft Learn `RIOSendEx`/`RIOReceiveEx` 문서 대조,
    `git diff --check`.
  - 비고: 다음 실행은 RIO UDP Task 1 native Ex operation shape 구현 계획이다.

- [x] RIO backend default promotion readiness 설계를 완료했다.
  - 범위: `docs/superpowers/specs/2026-06-25-rio-default-promotion-readiness-design.md`,
    `DECISIONS.md`, `docs/agent-state/decisions/2026-06.md`,
    `src/Hps.Transport/Runtime/TransportFactory.cs`, root 상태 문서.
  - 결과: D108로 `TransportFactory.CreateDefault()`는 계속 SAEA를 반환하고,
    RIO default 승격은 TCP/UDP parity readiness gate 이후 별도 결정으로만 진행한다고 정리했다.
  - 검증: factory 현재 behavior, RIO capability/benchmark opt-in path, RIO TCP tests 와 SAEA UDP/Broker coverage 대조,
    `git diff --check`.
  - 비고: 다음 실행은 RIO UDP backend boundary 설계다.

- [x] RIO payload cache 구현 self-review 를 완료했다.
  - 범위: `src/Hps.Transport.Rio/RioPayloadRegistrationCache.cs`,
    `docs/agent-state/reviews/2026-06-25-rio-payload-cache-self-review.md`, root 상태 문서.
  - 결과: major/blocker finding 은 없었다. self-review 중 idle eviction 의 native deregister 가 cache lock 내부에 남아 있는
    minor issue 를 발견해 정상 경로는 lock 밖 deregister 로 리팩터했다.
  - 검증: focused cache owner tests 4개 통과, focused RIO tests 34개 통과,
    common close/wake/pending tests 19개 통과, RIO close/handler close tests 2개 10회 반복 통과,
    `git diff --check`.
  - 비고: transport-wide shared payload cache 와 cache capacity diagnostics 는 deferred 로 유지한다.

- [x] RIO payload registration cache Task 2/3 send path cache lease 와 검증을 완료했다.
  - 범위: `src/Hps.Transport.Rio/RioTransport.cs`, `tests/Hps.Transport.Rio.Tests/RioTransportTcpTests.cs`, root 상태 문서.
  - 결과: `RioConnectionResource`가 connection-local payload cache 를 소유하고,
    payload send path 가 cache lease 로 registered buffer id 를 재사용한다.
  - 검증: Red payload loopback test `Expected: 1, Actual: 2` 확인, focused registration tests 3개 통과,
    focused RIO tests 34개 통과, close/wake 핵심 테스트 10회 반복 통과,
    solution tests 통과, solution build 0경고/0오류, `git diff --check`.
  - benchmark 관측: session-06 RIO load actual-rate 99.8 Hz/p50 288.4 us/p99 906.9 us,
    open-loop actual-rate 99.8 Hz/p50 293.8 us/p99 920.5 us.
  - 비고: 최초 build/test를 병렬 실행했을 때 `obj` 파일 잠금 경합으로 build만 실패했고,
    test 는 통과했다. build 단독 재실행은 0경고/0오류로 통과했다.

- [x] RIO payload registration cache Task 1 pure owner 를 구현했다.
  - 범위: `src/Hps.Transport.Rio/RioPayloadRegistrationCache.cs`,
    `tests/Hps.Transport.Rio.Tests/RioPayloadRegistrationCacheTests.cs`, root 상태 문서.
  - 결과: backing `byte[]` identity cache, idle LRU eviction, outstanding dispose-delayed deregister,
    all-outstanding capacity fallback lease 를 구현했다.
  - 검증: Red type boundary assertion failure 확인, focused cache owner tests 4개 통과, focused RIO tests 33개 통과.
  - 비고: 실제 RIO payload send path 연결은 Task 2 범위다.

- [x] RIO payload registration cache 구현 계획을 작성했다.
  - 범위: `docs/superpowers/plans/2026-06-25-rio-payload-registration-cache.md`, root 상태 문서.
  - 결과: D107 설계를 pure owner, send path cache lease, verification/benchmark/state update 의 3개 task 로 분해했다.
  - 검증: D107 spec coverage self-review, placeholder scan, type consistency scan, `git diff --check`.
  - 비고: 다음 실행은 Task 1 pure cache owner 다.

- [x] RIO payload `RefCountedBuffer` registration cache 설계를 작성했다.
  - 범위: `docs/superpowers/specs/2026-06-25-rio-payload-registration-cache-design.md`,
    `DECISIONS.md`, `docs/agent-state/decisions/2026-06.md`, root 상태 문서.
  - 결과: payload cache 는 transport-wide shared cache 가 아니라 connection resource bounded cache 로 먼저 구현한다(D107).
  - 검증: current payload send ownership, `RefCountedBuffer`/`PinnedBlockMemoryPool` lifetime, RIO Task A 결과 대조,
    placeholder scan, `git diff --check`.
  - 비고: transport-wide shared cache 는 fan-out evidence 이후 별도 설계 후보로 남긴다.

- [x] RIO registered buffer reuse Task A 를 구현했다.
  - 범위: `src/Hps.Transport.Rio/RioNative.cs`, `src/Hps.Transport.Rio/RioTransport.cs`,
    `tests/Hps.Transport.Rio.Tests/RioTransportTcpTests.cs`, root 상태 문서.
  - 결과: receive block 과 length-prefix block 을 connection resource lifetime 에 한 번 등록해 재사용한다.
    payload `RefCountedBuffer` send path 는 D106에 따라 per-operation registration 을 유지한다.
  - 검증: Red diagnostic tests 2개 실패 확인, focused diagnostic tests 2개 통과, focused RIO tests 29개 통과,
    close/wake 핵심 테스트 10회 반복 통과, solution build 0경고/0오류, solution tests 통과.
  - benchmark 관측: session-05 RIO load actual-rate 99.8 Hz/p50 281.6 us/p99 866.6 us,
    open-loop actual-rate 99.8 Hz/p50 315.8 us/p99 936.4 us.
  - 비고: payload registration cache 는 다음 설계 단위다.

- [x] RIO registered buffer reuse Task A 구현 계획을 작성했다.
  - 범위: `docs/superpowers/plans/2026-06-25-rio-registered-buffer-reuse-task-a.md`, root 상태 문서.
  - 결과: receive block registration, length-prefix registration, verification/benchmark observation 의 3개 task 로 분해했다.
  - 검증: spec coverage self-review, placeholder scan, `git diff --check`.
  - 비고: payload registration cache 는 별도 단위다.

- [x] RIO TCP close/churn stress coverage 를 추가했다.
  - 범위: `tests/Hps.Transport.Rio.Tests/RioTransportTcpTests.cs`, root 상태 문서.
  - 결과: connect/accept 직후 close 를 25회 반복해 receive pump 와 socket/CQ 정리 경합이 testhost crash 없이 끝나는지 검증한다.
  - 검증: focused RIO tests 22개 통과, focused RIO tests 10회 반복 통과.
  - 비고: full outstanding request owner 재구조화는 현재 반복 stress 에서 failure 가 재현되지 않아 deferred 로 유지한다.

- [x] RIO handler exception close notify 계약을 고정했다.
  - 범위: `tests/Hps.Transport.Rio.Tests/RioTransportTcpTests.cs`, root 상태 문서.
  - 결과: receive handler 예외가 background task fault 로 남지 않고 connection close notification 으로 수렴하는지 RIO loopback 에서 검증한다.
  - 검증: `dotnet test tests\Hps.Transport.Rio.Tests\Hps.Transport.Rio.Tests.csproj --no-restore`: 23개 통과.
  - 비고: 기존 RIO 구현이 이미 close notification 정책을 만족해 production 변경은 없었다.

- [x] RIO send queue/drop-oldest live saturation 테스트 후보를 D100으로 닫았다.
  - 범위: `DECISIONS.md`, `docs/agent-state/decisions/2026-06.md`, root 상태 문서.
  - 결과: RIO는 shared `TransportConnection` pending queue 를 그대로 쓰므로 drop-oldest ownership 은 공통 runtime 계약 테스트를 기준으로 검증한다.
  - 검증: 문서 정합성 검토, `TransportSendQueueTests` coverage 확인.
  - 비고: live RIO loopback queue saturation 은 OS socket drain 속도에 의존해 flake 가능성이 높으므로 별도 테스트로 추가하지 않는다.

- [x] RIO default factory opt-in policy 정합성을 재확인했다.
  - 범위: `src/Hps.Transport/Runtime/TransportFactory.cs`, `tests/Hps.Transport.Rio.Tests/RioCapabilityProbeTests.cs`, root 상태 문서.
  - 결과: 기본 `TransportFactory.CreateDefault()`는 계속 `SaeaTransport`를 반환하고,
    RIO는 D097/D098/D100에 따라 명시 opt-in/test path 로 유지한다.
  - 검증: factory 코드와 `CreateDefault_DuringRioOptInPhase_ReturnsSaeaTransport` 테스트 확인,
    focused RIO tests 23개 통과, solution build/test 292개 통과.
  - 비고: production 변경은 필요 없었다.

- [x] SAEA vs RIO benchmark comparison 설계를 완료했다.
  - 범위: `docs/superpowers/specs/2026-06-25-saea-rio-benchmark-comparison-design.md`,
    `DECISIONS.md`, `docs/agent-state/decisions/2026-06.md`, root 상태 문서.
  - 결과: benchmark 내부 `--backend <saea|rio>` selector 로 비교하고, raw report schema 는 backend 별 identity/scenario 값으로 구분한다.
  - 검증: benchmark CLI/result/summary/history source 대조, `git diff --check`.
  - 비고: repository RIO baseline 채택 구조, 비교 Markdown, latency hard gate 는 후속으로 둔다.

- [x] benchmark backend selector 를 구현했다.
  - 범위: `tests/Hps.Benchmarks/`, `tests/Hps.Benchmarks.Tests/`, root 상태 문서.
  - 결과: runner/baseline-suite 명령에서 `--backend <saea|rio>`를 파싱하고,
    `TcpLoopbackScenarioRunner`가 `SaeaTransport`/`RioTransport`와 backend 별 report identity 를 선택한다.
  - 검증: parser Red 확인, identity Red 확인, benchmark tests 71개 통과,
    SAEA/RIO smoke CLI pass 및 report `scenario`/`benchmark-profile`/`transport-backend` 확인.
  - 비고: RIO unavailable 환경에서 explicit RIO backend 는 fallback 하지 않고 실패한다.

- [x] SAEA/RIO benchmark comparison artifact 를 수집했다.
  - 범위: `artifacts/benchmarks/rio-comparison/2026-06-25/session-01/`, `.gitignore`, root 상태 문서.
  - 결과: SAEA/RIO load/open-loop raw report 와 mixed summary 를 scratch artifact 로 생성했다.
    `artifacts/`는 repository baseline 이 아니므로 `.gitignore`에 추가했다.
  - 검증: SAEA load/open-loop pass, RIO load/open-loop pass,
    mixed summary `hard-passed=true`, `warning-count=3`, `comparison-compatible=false`, comparison mismatch 6개 확인.
  - 비고: 주요 성능 신호는 SAEA load p99 890.8 us, SAEA open-loop p99 872.7 us,
    RIO load p99 16654.0 us, RIO open-loop p99 16826.6 us, RIO load actual-rate 64.5 Hz 다.

- [x] RIO completion wake/latency 개선 설계를 완료했다.
  - 범위: `docs/superpowers/specs/2026-06-25-rio-completion-wake-latency-design.md`,
    `DECISIONS.md`, `docs/agent-state/decisions/2026-06.md`, root 상태 문서.
  - 결과: IOCP/RIONotify 재구조화 전에 bounded `Task.Yield()` polling 으로 `Task.Delay(1)` timer granularity 병목을 먼저 줄이기로 했다(D102).
  - 검증: current RIO code, hardening design, comparison artifact evidence 대조, `git diff --check`.
  - 비고: IOCP/RIONotify, per-operation register buffer 비용, RIO repository baseline 채택 구조는 후속이다.

- [x] RIO completion wake bounded yield polling 을 구현했다.
  - 범위: `src/Hps.Transport.Rio/RioTransport.cs`, `src/Hps.Transport/Runtime/TransportConnection.cs`,
    `src/Hps.Transport/Saea/SaeaTransport.cs`, `tests/Hps.Transport.Rio.Tests/RioTransportTcpTests.cs`, root 상태 문서.
  - 결과: `WaitForCompletionAsync(...)`가 4096회까지 `Task.Yield()` polling 후 `Task.Delay(1)` fallback 을 사용한다.
    close notification 은 `TransportConnection.TryClose()` 전이에 성공한 pump 만 handler 를 호출하도록 SAEA/RIO를 정렬했다.
  - 검증: Red latency test failure 확인, focused RIO tests 24개 통과, Transport tests 43개 통과,
    RIO close/wake 핵심 테스트 10회 반복 통과, solution build 0경고/0오류, solution tests 통과.
  - benchmark 관측: D102 전 RIO load actual-rate 64.5 Hz/p50 15735 us/p99 16654 us.
    4096 budget 후 RIO load actual-rate 99.8 Hz/p50 198.8 us/p99 16689.0 us,
    RIO open-loop p50 397.2 us/p99 16736.2 us.
  - 비고: p50/throughput 은 개선됐지만 p99 tail 은 남았으므로 IOCP/RIONotify completion wait 설계로 후속화한다.

- [x] RIO IOCP/RIONotify completion wait 를 설계했다.
  - 범위: `docs/superpowers/specs/2026-06-25-rio-iocp-notification-completion-wait-design.md`,
    `DECISIONS.md`, `docs/agent-state/decisions/2026-06.md`, root 상태 문서.
  - 결과: CQ별 event handle 대신 `RioTransport`당 shared IOCP pump 와 CQ별 `RioCompletionSignal` 구조를 채택했다(D104).
  - 검증: Microsoft RIO notification 문서, current RIO native wrapper/resource gate,
    D102 benchmark evidence 대조, placeholder scan, `git diff --check`.
  - 비고: 구현은 native shape, completion signal, resource wiring, hardening/benchmark task 로 계획을 먼저 작성한다.

- [x] RIO IOCP/RIONotify completion wait 구현 계획을 작성했다.
  - 범위: `docs/superpowers/plans/2026-06-25-rio-iocp-notification-completion-wait.md`, root 상태 문서.
  - 결과: D104 설계를 native notification shape, completion port/signal owner,
    RIONotify+IOCP wiring, benchmark observation/state update 의 4개 task 로 분해했다.
  - 검증: spec coverage self-review, placeholder scan, `git diff --check`.
  - 비고: 다음 실행은 Task 1 `RioNative` notification shape 다.

- [x] RIO IOCP/RIONotify completion wait Task 1 native notification shape 를 구현했다.
  - 범위: `src/Hps.Transport.Rio/RioNative.cs`, `tests/Hps.Transport.Rio.Tests/RioCapabilityProbeTests.cs`, root 상태 문서.
  - 결과: `RioNative`가 `RIONotify`, notification CQ overload, IOCP P/Invoke/struct shape,
    `SupportsCompletionNotification` probe 를 노출한다.
  - 검증: Red `SupportsCompletionNotification` assertion failure 확인,
    focused test green, focused RIO tests 25개 통과, solution build 0경고/0오류.
  - 비고: 실제 shared IOCP pump wiring 은 Task 3이며, Task 2는 managed signal owner lifecycle 을 먼저 고정한다.

- [x] RIO IOCP/RIONotify completion wait Task 2 completion port/signal owner 를 구현했다.
  - 범위: `src/Hps.Transport.Rio/RioCompletionPort.cs`, `src/Hps.Transport.Rio/RioCompletionSignal.cs`,
    `tests/Hps.Transport.Rio.Tests/RioCompletionPortTests.cs`, root 상태 문서.
  - 결과: managed signal registry, waiter wake, dispose wake, pump fault boundary 를 추가했다.
    아직 실제 native IOCP handle 과 `RIONotify`에는 연결하지 않는다.
  - 검증: Red type absence assertion failure 확인, focused completion port tests 2개 통과,
    focused RIO tests 27개 통과, solution build 0경고/0오류.
  - 비고: 다음 Task 3에서 notification CQ creation 과 IOCP pump 를 실제 transport resource 에 연결한다.

- [x] RIO IOCP/RIONotify completion wait Task 3 RIONotify + IOCP wiring 을 구현했다.
  - 범위: `src/Hps.Transport.Rio/RioCompletionPort.cs`, `src/Hps.Transport.Rio/RioCompletionSignal.cs`,
    `src/Hps.Transport.Rio/RioTransport.cs`, `tests/Hps.Transport.Rio.Tests/RioCompletionPortTests.cs`, root 상태 문서.
  - 결과: `WaitForCompletionAsync(...)`는 더 이상 timer polling fallback 을 사용하지 않고,
    `RIONotify`를 arm 한 뒤 shared IOCP pump 가 깨우는 signal 을 기다린다.
  - 검증: focused RIO tests 27개 통과, close/wake 핵심 테스트 10회 반복 통과,
    solution build 0경고/0오류, solution tests 통과.
  - benchmark 관측: D102 session-03 RIO load p99 16689.0 us/open-loop p99 16736.2 us 에서
    session-04 RIO load p99 739.5 us/open-loop p99 948.8 us 로 개선됐다.
  - 비고: per-operation buffer registration 비용, multi-result drain, batching 은 별도 후속 최적화 후보로 분리한다.

- [x] RIO IOCP/RIONotify completion wait Task 4 benchmark observation/state update 를 완료했다.
  - 범위: `CURRENT_PLAN.md`, `TODOS.md`, `CHANGELOG_AGENT.md`, `DECISIONS.md`,
    `docs/agent-state/decisions/2026-06.md`.
  - 결과: session-04 benchmark 결과를 D105로 기록하고, 다음 최적화 후보를 registered buffer reuse 로 분리했다.
  - 검증: focused RIO tests 27개 통과, close/wake 핵심 테스트 10회 반복 통과,
    solution build 0경고/0오류, solution tests 통과, benchmark artifact 확인, `git diff --check`.
  - 비고: session-04 scratch artifact 는 ignored `artifacts/benchmarks/rio-comparison/2026-06-25/session-04/`에 남아 있다.

- [x] RIO registered buffer reuse 를 설계했다.
  - 범위: `docs/superpowers/specs/2026-06-25-rio-registered-buffer-reuse-design.md`,
    `DECISIONS.md`, `docs/agent-state/decisions/2026-06.md`, root 상태 문서.
  - 결과: receive block 과 length-prefix block 은 resource lifetime registration 으로 먼저 처리하고,
    payload `RefCountedBuffer` registration cache 는 별도 단위로 분리하기로 했다(D106).
  - 검증: Microsoft RIO register/deregister/send/receive 문서와 current code ownership 대조, `git diff --check`.
  - 비고: 다음은 Task A 구현 계획 작성이다.

- [x] RIO TCP pump hardening 설계와 send completion 보강을 완료했다.
  - 범위: `src/Hps.Transport.Rio/RioTransport.cs`, `tests/Hps.Transport.Rio.Tests/RioTransportTcpTests.cs`,
    `docs/superpowers/specs/2026-06-25-rio-tcp-pump-hardening-design.md`,
    `docs/superpowers/plans/2026-06-25-rio-tcp-pump-hardening.md`, root 상태 문서.
  - 결과: RIO send path 가 completion byte count 를 기준으로 remaining loop 를 돌며,
    length prefix 와 payload 모두 같은 helper 를 사용한다.
  - 테스트: raw payload, 4096-byte payload, length-prefixed stream send 를 RIO available loopback 으로 검증한다.
    length-prefix Red 에서 TCP stream 이 prefix 와 payload 를 별도 callback 으로 전달할 수 있음을 확인하고,
    test receive helper 를 expected length 누적 방식으로 보정했다.
  - 검증: focused RIO tests 21개 통과, focused RIO tests 10회 반복 통과.

- [x] RIO Task 6 구현 self-review 를 완료했다.
  - 범위: `src/Hps.Transport.Rio/`, `tests/Hps.Transport.Rio.Tests/`, `docs/agent-state/reviews/2026-06-25-rio-task6-self-review.md`, root 상태 문서.
  - 결과: Task 6 구현은 최소 opt-in RIO TCP loopback 계약을 만족하지만, send partial completion loop 와
    outstanding request close-drain 모델은 factory default 승격 전에 hardening 이 필요하다고 정리했다.
  - 검증: focused RIO tests 10회 반복 실행 모두 통과.

- [x] RIO Task 6 TCP pump/contract test reuse 를 구현했다.
  - 범위: `src/Hps.Transport.Rio/RioTransport.cs`, `src/Hps.Transport.Rio/RioConnectionListener.cs`,
    `src/Hps.Transport/Properties/AssemblyInfo.cs`, `tests/Hps.Transport.Rio.Tests/RioTransportTcpTests.cs`, root 상태 문서.
  - 결과: RIO available Windows 환경에서 opt-in `RioTransport`가 실제 TCP listen/connect/accept 후
    `TrySend` payload 를 peer receive handler 로 전달한다.
  - 비고: accepted socket 은 일반 `AcceptAsync()` 반환값으로는 RIO RQ 생성이 실패하므로,
    `RioNative.CreateTcpSocket()`으로 만든 registered accept socket 을 `AcceptAsync(Socket, CancellationToken)`에 전달한다(D099).
  - 검증: Red `NotSupportedException` 실패 확인, 일반 accept socket RQ 생성 실패 확인 후 registered accept socket 으로 보정했다.
    전체 테스트 1차 실행 중 CQ close/dequeue 경합으로 `RIODequeueCompletion` access violation 이 발생해
    resource gate 로 직렬화했고, focused RIO tests 19개와 solution tests 288개 통과를 확인했다.

- [x] CI baseline adoption 이후 Phase 4 다음 후보를 재평가했다.
  - 범위: `docs/superpowers/specs/2026-06-25-after-ci-baseline-adoption-reassessment-design.md`,
    `DECISIONS.md`, `docs/agent-state/decisions/2026-06.md`, `docs/benchmarks/baselines/index.md`, root 상태 문서.
  - 결과: `ci-windows-x64-01/2026-06-25/session-01`은 hard-passed true, warning-count 0,
    comparison-compatible true 이지만 date root 1개/session 1개뿐이므로 latency hard gate 또는
    warning-as-failure 로 승격하지 않는다(D096).
  - 비고: CI runner evidence 는 future push-triggered run 이 더 쌓이면 D095 checklist 로 수동 채택 여부를 다시 판단한다.
    다음 실행 가능한 큰 흐름은 Phase 5 Windows RIO backend 설계다.
  - 검증: CI runner root history, session summary, baseline index, D082/D090/D095를 대조했다.

- [x] Phase 5 Windows RIO backend boundary 를 설계했다.
  - 범위: `docs/superpowers/specs/2026-06-25-windows-rio-backend-boundary-design.md`,
    `DECISIONS.md`, `docs/agent-state/decisions/2026-06.md`, root 상태 문서.
  - 결과: RIO backend 는 TCP-first 로 진행하되, 첫 구현 task 를 project skeleton,
    Windows capability probe, native function table wrapper 로 분리했다(D097).
  - 비고: 기본 `TransportFactory.CreateDefault()`는 SAEA를 유지하고, RIO는 명시 opt-in/test path 로 먼저 검증한다.
    UDP RIO, batching, automatic default backend selection 은 후속으로 둔다.
  - 검증: current transport 구조, 빈 RIO project 상태, Microsoft RIO 문서를 대조했다.

- [x] Phase 5 Windows RIO backend 구현 계획을 작성했다.
  - 범위: `docs/superpowers/plans/2026-06-25-windows-rio-backend.md`, root 상태 문서.
  - 결과: D097 설계를 project skeleton/capability probe, native function table loader,
    registered buffer owner, TCP queue owner, TCP opt-in guard, TCP pump/contract test reuse 의 6개 task 로 나눴다.
  - 비고: Task 1 Red는 production type 부재를 reflection assertion failure 로 검증하도록 보정했다.
  - 검증: plan self-review, placeholder scan, current transport 구조 대조.

- [x] RIO Task 1 project skeleton 과 capability probe 를 구현했다.
  - 범위: `src/Hps.Transport.Rio/`, `tests/Hps.Transport.Rio.Tests/`, `HighPerformanceSocket.slnx`, root 상태 문서.
  - 결과: `RioCapabilityStatus`, `RioCapabilityProbe.GetStatus()`, `RioTransport` skeleton 을 추가했다.
    non-Windows 는 `UnsupportedOperatingSystem`, Windows 는 native loader 구현 전까지 `Unavailable`로 보고한다.
  - 비고: 기본 `TransportFactory.CreateDefault()`는 계속 `SaeaTransport`를 반환한다.
  - 검증: Red assertion failure 1개 확인(`Assert.NotNull() Failure: Value is null`),
    focused RIO tests 4개 통과, solution build 경고 0/오류 0.

- [x] RIO Task 2 native function table loader 를 구현했다.
  - 범위: `src/Hps.Transport.Rio/RioNative.cs`, `src/Hps.Transport.Rio/RioCapabilityProbe.cs`,
    `tests/Hps.Transport.Rio.Tests/RioCapabilityProbeTests.cs`, root 상태 문서.
  - 결과: `RioNative.TryLoadFunctionTable(out RioNative?)` 경계를 추가하고,
    `RioCapabilityProbe.GetStatus()`가 해당 경계를 통해 `Available` 또는 `Unavailable`을 반환하도록 연결했다.
  - 비고: 실제 `WSAIoctl`/`WSAID_MULTIPLE_RIO` marshalling 은 아직 넣지 않고, 예외 없는 fallback 경계를 먼저 고정했다.
  - 검증: Red assertion failure 1개 확인(`Assert.NotNull() Failure: Value is null`),
    focused RIO tests 6개 통과, solution build 경고 0/오류 0.

- [x] RIO Task 3 registered buffer owner 를 구현했다.
  - 범위: `src/Hps.Transport.Rio/RioRegisteredBufferPool.cs`,
    `tests/Hps.Transport.Rio.Tests/RioRegisteredBufferPoolTests.cs`,
    `src/Hps.Transport.Rio/Properties/AssemblyInfo.cs`, root 상태 문서.
  - 결과: outstanding request 완료 전에는 block 을 반환하지 않고, 중복 completion 에서는 release 를 한 번만 수행한다.
  - 비고: Red 용 reflection 테스트는 Green 이후 `InternalsVisibleTo` 기반 direct internal API 테스트로 정리했다.
  - 검증: Red assertion failure 1개 확인(`Assert.NotNull() Failure: Value is null`),
    focused RIO tests 7개 통과, solution build 경고 0/오류 0.

- [x] RIO Task 4 TCP queue owners 를 구현했다.
  - 범위: `src/Hps.Transport.Rio/RioCompletionQueue.cs`, `src/Hps.Transport.Rio/RioRequestQueue.cs`,
    `tests/Hps.Transport.Rio.Tests/RioQueueOwnerTests.cs`, root 상태 문서.
  - 결과: receive/send quota reservation 을 독립적으로 제한하고 completion 후 quota 를 다시 열 수 있게 했다.
  - 비고: Red 용 reflection 테스트는 Green 이후 `InternalsVisibleTo` 기반 direct internal API 테스트로 정리했다.
  - 검증: Red assertion failure 2개 확인(`Assert.NotNull() Failure: Value is null`),
    focused RIO tests 9개 통과, solution build 경고 0/오류 0.

- [x] RIO Task 5 TCP opt-in transport guard 를 구현했다.
  - 범위: `src/Hps.Transport.Rio/RioTransport.cs`,
    `tests/Hps.Transport.Rio.Tests/RioTransportTcpTests.cs`, root 상태 문서.
  - 결과: RIO unavailable 환경에서 `ListenTcpAsync`/`ConnectTcpAsync`가 actual TCP wiring 미구현 메시지보다 먼저
    Windows RIO function table 사용 불가를 명시하는 `NotSupportedException`으로 실패한다.
  - 비고: 기본 `TransportFactory.CreateDefault()`/SAEA 경로와 실제 RIO socket pump 는 건드리지 않았다.
  - 검증: Red assertion failure 1개 확인(`Sub-string not found`),
    focused RIO tests 10개 통과, solution build 경고 0/오류 0, solution tests 279개 통과.

- [x] RIO Task 5.5 native function table loader hardening 을 구현했다.
  - 범위: `src/Hps.Transport.Rio/RioNative.cs`,
    `tests/Hps.Transport.Rio.Tests/RioCapabilityProbeTests.cs`,
    `docs/superpowers/plans/2026-06-25-windows-rio-backend.md`, decision/root 상태 문서.
  - 결과: `RioNative`가 Windows에서 `WSAIoctl(SIO_GET_MULTIPLE_EXTENSION_FUNCTION_POINTER, WSAID_MULTIPLE_RIO)`로
    실제 `RIO_EXTENSION_FUNCTION_TABLE`을 얻고 필수 pointer 를 검증한다.
  - 비고: D098로 Task 6 전에 실제 native loader 를 완료해야 한다는 순서 보정을 기록했다.
  - 검증: Red assertion failure 1개 확인(`Expected: Available`, `Actual: Unavailable`),
    focused RIO tests 11개 통과, solution build 경고 0/오류 0, solution tests 280개 통과.

- [x] RIO Task 5.6 native buffer registration delegate 를 구현했다.
  - 범위: `src/Hps.Transport.Rio/RioNative.cs`,
    `tests/Hps.Transport.Rio.Tests/RioCapabilityProbeTests.cs`,
    `docs/superpowers/plans/2026-06-25-windows-rio-backend.md`, root 상태 문서.
  - 결과: loaded RIO function table 에서 `RIORegisterBuffer`/`RIODeregisterBuffer`를 delegate 로 marshal 하고,
    `RioNative.RegisterBuffer(...)`/`DeregisterBuffer(...)` internal operation 으로 노출했다.
  - 비고: Red는 reflection assertion failure 로 시작했고, Green 이후 direct internal API 테스트로 정리했다.
  - 검증: Red assertion failure 1개 확인(`Assert.NotNull() Failure: Value is null`),
    focused RIO tests 12개 통과, solution build 경고 0/오류 0, solution tests 281개 통과.

- [x] RIO Task 5.7 native completion queue delegate 를 구현했다.
  - 범위: `src/Hps.Transport.Rio/RioNative.cs`,
    `tests/Hps.Transport.Rio.Tests/RioCapabilityProbeTests.cs`,
    `docs/superpowers/plans/2026-06-25-windows-rio-backend.md`, root 상태 문서.
  - 결과: loaded RIO function table 에서 `RIOCreateCompletionQueue`/`RIOCloseCompletionQueue`를 delegate 로 marshal 하고,
    `RioNative.CreateCompletionQueue(...)`/`CloseCompletionQueue(...)` internal operation 으로 노출했다.
  - 비고: 초기 pump 는 null notification completion 기반 polling/dequeue 모델로 검증한다.
  - 검증: Red assertion failure 1개 확인(`Assert.NotNull() Failure: Value is null`),
    focused RIO tests 13개 통과, solution build 경고 0/오류 0, solution tests 282개 통과.

- [x] RIO Task 5.8 native request queue delegate 를 구현했다.
  - 범위: `src/Hps.Transport.Rio/RioNative.cs`,
    `tests/Hps.Transport.Rio.Tests/RioCapabilityProbeTests.cs`,
    `docs/superpowers/plans/2026-06-25-windows-rio-backend.md`, root 상태 문서.
  - 결과: `WSASocketW` + `WSA_FLAG_OVERLAPPED | WSA_FLAG_REGISTERED_IO` 기반 TCP socket factory 와
    `RIOCreateRequestQueue` delegate operation 을 추가했다.
  - 비고: 일반 .NET `Socket`으로 RQ 생성 시 null handle 이 반환되어 registered I/O socket 생성 경계가 필요함을 확인했다.
  - 검증: Red assertion failure 1개 확인(`Assert.NotNull() Failure: Value is null`),
    Green 중 일반 socket RQ null handle 실패를 확인한 뒤 registered I/O socket 으로 보정,
    focused RIO tests 14개 통과, solution build 경고 0/오류 0, solution tests 283개 통과.

- [x] RIO Task 5.9 native completion dequeue delegate 를 구현했다.
  - 범위: `src/Hps.Transport.Rio/RioNative.cs`,
    `tests/Hps.Transport.Rio.Tests/RioCapabilityProbeTests.cs`,
    `docs/superpowers/plans/2026-06-25-windows-rio-backend.md`, root 상태 문서.
  - 결과: `RIODequeueCompletion` delegate 와 SDK `RIORESULT`에 맞춘 `RioResult` marshalling 을 추가했다.
  - 비고: 빈 CQ에서 0개 completion 을 반환하는 경계로 dequeue 호출을 검증했다.
  - 검증: Red assertion failure 1개 확인(`Assert.NotNull() Failure: Value is null`),
    focused RIO tests 15개 통과, solution build 경고 0/오류 0, solution tests 284개 통과.

- [x] RIO Task 5.10 native receive/send posting delegate surface 를 구현했다.
  - 범위: `src/Hps.Transport.Rio/RioNative.cs`,
    `tests/Hps.Transport.Rio.Tests/RioCapabilityProbeTests.cs`,
    `docs/superpowers/plans/2026-06-25-windows-rio-backend.md`, root 상태 문서.
  - 결과: `RIOReceive`/`RIOSend` delegate 와 SDK `RIO_BUF`에 맞춘 `RioBufferSegment` marshalling 을 추가했다.
  - 비고: 이번 단위는 operation surface 와 argument validation 만 고정하고, 실제 connected post completion 은 다음 단위로 분리했다.
  - 검증: Red assertion failure 1개 확인(`Assert.NotNull() Failure: Value is null`),
    focused RIO tests 16개 통과, solution build 경고 0/오류 0, solution tests 285개 통과.

- [x] RIO Task 5.11 connected native posting completion 을 검증했다.
  - 범위: `tests/Hps.Transport.Rio.Tests/RioCapabilityProbeTests.cs`,
    `docs/superpowers/plans/2026-06-25-windows-rio-backend.md`, root 상태 문서.
  - 결과: loopback peer 와 연결한 registered I/O socket 에서 `RIOReceive` post completion 과 registered buffer write,
    `RIOSend` post completion 과 peer receive 를 모두 확인했다.
  - 비고: production 변경 없이 Task 5.6~5.10 native operation boundary 의 통합 동작을 검증한 test-hardening 단위다.
  - 검증: focused RIO tests 18개 통과, solution build 경고 0/오류 0, solution tests 287개 통과.

- [x] CI push-triggered artifact `28145025444`를 repository baseline 으로 수동 채택했다.
  - 범위: `docs/benchmarks/baselines/runners/ci-windows-x64-01/2026-06-25/session-01/`,
    date-level history, runner root history, `docs/benchmarks/baselines/index.md`, root 상태 문서.
  - 결과: artifact zip/root directory 는 커밋하지 않고 raw report 6개만 복사했다.
    summary/history 는 repository 경로 기준으로 재생성했다.
  - 비고: CI runner root history 는 session-count 1, hard-passed true, warning-count 0,
    comparison-compatible true 다. CI runner first reference envelope 는 load p99 max 275.3 us,
    open-loop p99 max 322.9 us, TCP HWM max 2 다.
  - 검증: D095 checklist, summary/history 재생성, absolute path scan 결과 없음,
    `git diff --check` exit 0, benchmark tests 67개 통과, solution build 경고 0/오류 0,
    solution tests 269개 통과.

- [x] CI artifact adoption 절차를 설계했다.
  - 범위: `docs/superpowers/specs/2026-06-25-ci-artifact-adoption-policy-design.md`, D095, root 상태 문서.
  - 결과: CI artifact 는 자동 채택하지 않고, checklist 통과 artifact 의 raw report 6개만 repository baseline 구조로 수동 채택한다.
  - 비고: warning-count > 0 artifact 는 repository reference baseline 으로 채택하지 않는다.
    첫 채택 후보는 D094 push trigger 로 생성된 run `28145025444`다.
  - 검증: D090/D093/D094, `docs/benchmarks/baselines/index.md`, downloaded artifact 구조를 대조했다.

- [x] D094 trigger policy push 후 자동 CI artifact run 을 검증했다.
  - 범위: GitHub Actions run `28145025444`, artifact
    `benchmark-artifacts-ci-windows-x64-01-2026-06-25-github-28145025444-1`, root 상태 문서.
  - 결과: `push` event 로 `Benchmark Artifacts` run 이 자동 생성됐고 성공했다.
  - 비고: 로그에서 `actions/checkout@v7`, `actions/setup-dotnet@v5.3.0`, `actions/upload-artifact@v7.0.1` 다운로드/실행을 확인했다.
    `deprecation`, `Node.js 20`, `node20`, 이전 `actions/*@v4` 문자열 검색 결과는 없었다.
    artifact 는 raw report 6개, `summary.json`, `summary.md`, `history.json`, `history.md` 총 10개 파일을 포함한다.
    `summary.json`은 `source-report-count=6`, `hard-passed=true`, `warning-count=0`,
    `comparison-compatible=true`, `unknown-runner-count=0`이다.
    `history.json`은 `session-count=1`, `hard-passed=true`, `warning-count=0`, `comparison-compatible=true`다.
  - 검증: `gh run list`, `gh run watch --exit-status`, `gh run view --log`, `gh run download`로
    push-triggered run 성공과 artifact 내용을 확인했다.

- [x] CI artifact trigger policy 를 설계하고 workflow 에 반영했다.
  - 범위: `.github/workflows/benchmark-artifacts.yml`,
    `docs/superpowers/specs/2026-06-25-ci-artifact-trigger-policy-design.md`, D094, root 상태 문서.
  - 결과: `workflow_dispatch`는 유지하고, `push` to `master` 중 code/benchmark/build 관련 path 변경에만 자동 실행하도록 했다.
  - 비고: `pull_request`와 `schedule`은 아직 추가하지 않는다. docs-only 변경은 benchmark artifact 를 만들지 않는다.
  - 검증: workflow marker scan, trigger out-of-scope scan, `git diff --check`로 확인한다.

- [x] CI artifact-only manual run 2회 결과 이후 Phase 4 다음 후보를 재평가했다.
  - 범위: run `28143728630`, run `28144480160`, D090/D091/D092, baseline index, root 상태 문서.
  - 결과: latency gate, warning-as-failure, docs baseline 자동 채택, push/PR 자동 trigger 는 승격하지 않는다(D093).
  - 비고: 두 run 모두 성공했지만 같은 날짜의 GitHub-hosted Windows evidence 이며, 첫 run 은 warning-count 1,
    두 번째 run 은 warning-count 0이었다. 이 상태는 CI runner scheduling noise 가능성을 보여주므로
    gate 승격에는 부족하다.
  - 다음: CI artifact trigger policy 를 설계한다.
  - 검증: run log/artifact, D090/D091/D092, `docs/benchmarks/baselines/index.md`, current backlog 를 대조했다.

- [x] Node 24 action 갱신 후 CI artifact-only workflow manual run 을 재검증했다.
  - 범위: GitHub Actions run `28144480160`, artifact
    `benchmark-artifacts-ci-windows-x64-01-2026-06-25-github-28144480160-1`, root 상태 문서.
  - 결과: workflow 는 성공했다. restore/build/test, `baseline-suite`, `summary`, `history`, artifact upload 단계가 모두 통과했다.
  - 비고: 로그에서 `actions/checkout@v7`, `actions/setup-dotnet@v5.3.0`, `actions/upload-artifact@v7.0.1` 다운로드/실행을 확인했다.
    `deprecation`, `Node.js 20`, `node20`, 이전 `actions/*@v4` 문자열 검색 결과는 없었다.
    artifact 는 raw report 6개, `summary.json`, `summary.md`, `history.json`, `history.md` 총 10개 파일을 포함한다.
    `summary.json`은 `source-report-count=6`, `hard-passed=true`, `warning-count=0`,
    `comparison-compatible=true`, `unknown-runner-count=0`이다.
    `history.json`은 `session-count=1`, `hard-passed=true`, `warning-count=0`, `comparison-compatible=true`다.
  - 검증: `gh workflow run`, `gh run watch --exit-status`, `gh run view --log`, `gh run download`로
    run 성공과 artifact 내용을 확인했다.

- [x] GitHub Actions Node 20 deprecation annotation 대응을 처리했다.
  - 범위: `.github/workflows/benchmark-artifacts.yml`, D092 decision, CI workflow plan/policy 문서, root 상태 문서.
  - 결과: 첫 manual run `28143728630`에서 확인된 `actions/*@v4` Node.js 20 annotation 에 대응해
    `actions/checkout@v7`, `actions/setup-dotnet@v5.3.0`, `actions/upload-artifact@v7.0.1`로 갱신했다.
  - 비고: 2026-06-25 공식 release/action metadata 확인 기준 세 action version 은 `runs.using: node24`를 명시한다.
    benchmark command, artifact path, D090 report-only warning policy 는 바꾸지 않았다.
  - 검증: workflow static marker scan, `git diff --check` exit 0, solution tests 269개 통과,
    solution build 단독 재실행 경고 0/오류 0.

- [x] CI artifact-only workflow 첫 manual run 결과를 확인했다.
  - 범위: GitHub Actions run `28143728630`, artifact
    `benchmark-artifacts-ci-windows-x64-01-2026-06-25-github-28143728630-1`, root 상태 문서.
  - 결과: workflow 는 성공했다. restore/build/test, `baseline-suite`, `summary`, `history`, artifact upload 단계가 모두 통과했다.
  - 비고: artifact 는 raw report 6개, `summary.json`, `summary.md`, `history.json`, `history.md` 총 10개 파일을 포함한다.
    `summary.json`은 `source-report-count=6`, `hard-passed=true`, `warning-count=1`,
    `comparison-compatible=true`, `unknown-runner-count=0`이다.
    `history.json`은 `session-count=1`, `hard-passed=true`, `warning-count=1`, `comparison-compatible=true`다.
    warning 은 `open-loop-01.json`의 `p99-growth-ratio-high`이며 D090 기준 report-only 다.
  - 검증: `gh workflow run`, `gh run watch --exit-status`, `gh run download`로 run 성공과 artifact 내용을 확인했다.
    `git diff --check` exit 0, solution build 경고 0/오류 0, solution tests 269개 통과.

- [x] CI workflow benchmark command sequence 를 local smoke 로 검증하고 no-restore 로 보정했다.
  - 범위: `.github/workflows/benchmark-artifacts.yml`,
    `docs/superpowers/plans/2026-06-25-ci-artifact-only-workflow-skeleton.md`, root 상태 문서.
  - 결과: workflow 의 benchmark CLI 세 단계에 모두 `--no-build --no-restore`를 명시했다.
  - 비고: 최초 full smoke 는 workflow command sequence 로 `--runs 3`을 실행해 raw report 6개,
    `summary.json`/`summary.md`, `history.json`/`history.md` 생성을 확인했다. 이때 첫 benchmark `dotnet run`이
    restore를 다시 시도하며 `NU1900` 경고를 냈으므로, 이미 완료된 restore/build/test 를 재사용하도록 no-restore 형태로 보정했다.
  - 검증: 보정 후 `--runs 1` local smoke 에서 raw report 2개, summary/history artifact 생성,
    hard-passed true, warning-count 0을 확인했다. local smoke 후 sandbox NuGet cache 경로로 바뀐 restore asset 은
    `dotnet restore HighPerformanceSocket.slnx`로 복구했다. `git diff --check` exit 0,
    solution build 경고 0/오류 0, solution tests 269개 통과.

- [x] CI artifact-only workflow skeleton 을 구현했다.
  - 범위: `.github/workflows/benchmark-artifacts.yml`, `CURRENT_PLAN.md`, `TODOS.md`, `CHANGELOG_AGENT.md`.
  - 결과: `workflow_dispatch` 전용 GitHub Actions workflow 를 추가했다.
    job env 는 `HPS_BENCHMARK_RUNNER_ID=ci-windows-x64-01`, `HPS_BENCHMARK_RUNNER_KIND=ci`로 고정한다.
  - 비고: workflow 는 restore/build/test 이후 `baseline-suite`, `summary`, `history`를 실행하고
    date root 를 `actions/upload-artifact@v7.0.1`로 업로드한다. 자동 push/PR trigger 와 warning/latency failure logic 은 넣지 않았다.
  - 검증: workflow static marker scan 과 lightweight policy check 를 통과했다.
    `git diff --check` exit 0, solution build 경고 0/오류 0, solution tests 269개 통과.

- [x] CI artifact-only workflow skeleton 구현 계획을 작성했다.
  - 범위: `docs/superpowers/plans/2026-06-25-ci-artifact-only-workflow-skeleton.md`,
    `DECISIONS.md`, `docs/agent-state/decisions/2026-06.md`, root 상태 문서.
  - 결과: workflow trigger 는 `workflow_dispatch` 전용으로 시작하고,
    `HPS_BENCHMARK_RUNNER_ID=ci-windows-x64-01`, `HPS_BENCHMARK_RUNNER_KIND=ci`를 job env 로 둔다.
  - 비고: 현재 `BaselineHistoryReader`가 `session-NN`만 history session 으로 읽기 때문에,
    GitHub run id 는 upload artifact 이름에만 넣고 내부 디렉터리는 `<yyyy-mm-dd>/session-01/`로 유지한다(D091).
  - 검증: D090 spec, benchmark CLI, `BaselineHistoryReader`, `.github/workflows` 부재를 대조했다.
    placeholder scan 신규 미정 항목 없음, `git diff --check` exit 0, solution build 경고 0/오류 0,
    solution tests 269개 통과.

- [x] CI artifact-only benchmark 정책을 설계했다.
  - 범위: `docs/superpowers/specs/2026-06-25-ci-artifact-only-benchmark-policy-design.md`,
    `docs/benchmarks/baselines/index.md`, `DECISIONS.md`, `docs/agent-state/decisions/2026-06.md`, root 상태 문서.
  - 결과: CI runner id 는 `ci-windows-x64-01`, runner kind 는 `ci`를 권장하고,
    매 실행 artifact 는 docs baseline 과 섞지 않는 `artifacts/benchmarks/runners/<ci-runner-id>/...` 영역으로 분리한다.
  - 비고: CI 실패 조건은 build/test, command usage/write failure, delivery/drop/leak hard gate 실패로 제한한다.
    latency/HWM/warning 은 report-only 이며 `warning-count > 0`만으로 실패하지 않는다.
  - 검증: benchmark `Program` exit code 규칙, `BenchmarkRunIdentity` 환경 변수 규칙, `.github/workflows` 부재를 대조했다.
    `git diff --check` exit 0, solution build 경고 0/오류 0, solution tests 269개 통과.

- [x] explicit runner 2-date-root reference 이후 Phase 4 gate 승격 후보를 재평가했다.
  - 범위: `docs/superpowers/specs/2026-06-25-phase4-gate-promotion-reassessment-design.md`,
    `DECISIONS.md`, `docs/agent-state/decisions/2026-06.md`, root 상태 문서.
  - 결과: `local-win-x64-01`은 두 date root, 6-session compatible reference 를 갖췄지만
    D082의 서로 다른 date root 3개 이상 조건과 별도 warning threshold 검토 조건은 아직 충족하지 못했다.
  - 비고: warning-as-failure 와 CI latency hard gate 는 계속 보류한다. 세 번째 date root 는 실제 다음 측정 날짜에 수집한다.
    다음 실행 가능한 문서 단위는 CI artifact-only benchmark 정책 설계다.
  - 검증: runner root history 와 `docs/benchmarks/baselines/index.md` 수치를 대조했다.
    D082 조건 충족/미충족 상태를 설계 문서에 명시했다. `git diff --check` exit 0,
    solution build 경고 0/오류 0, solution tests 269개 통과.

- [x] `local-win-x64-01/2026-06-25/session-03` explicit runner baseline 을 수집했다.
  - 범위: `docs/benchmarks/baselines/runners/local-win-x64-01/2026-06-25/session-03/`,
    `docs/benchmarks/baselines/runners/local-win-x64-01/2026-06-25/history.json`,
    `docs/benchmarks/baselines/runners/local-win-x64-01/history.json`,
    `docs/benchmarks/baselines/index.md`, root 상태 문서.
  - 결과: raw report 6개, `summary.json`, `summary.md`를 생성하고,
    date-level `history.json`/`history.md`, runner root `history.json`/`history.md`를 재생성했다.
  - 비고: 2026-06-25 date root 는 session-count 3, hard-passed true, warning-count 0,
    comparison-compatible true 다. runner root 는 session-count 6, hard-passed true,
    warning-count 0, comparison-compatible true 다. explicit runner envelope 는 load p99 max 935.6 us,
    open-loop p99 max 1077.4 us 이다. 같은 runner 의 두 date root 가 각각 3-session reference 를 갖췄으므로
    다음 단위는 D082 gate 승격 후보 재평가다.
  - 검증: baseline suite pass, summary CLI source-report-count 6/hard-passed true/warning-count 0,
    date history CLI session-count 3/hard-passed true/warning-count 0,
    runner history CLI session-count 6/hard-passed true/warning-count 0.
    runner artifact local absolute path 검색 결과 없음. `Hps.Benchmarks.Tests` 67개 통과,
    `git diff --check` exit 0, restore asset 재생성 후 solution build 경고 0/오류 0,
    solution tests 269개 통과.

- [x] `local-win-x64-01/2026-06-25/session-02` explicit runner baseline 을 수집했다.
  - 범위: `docs/benchmarks/baselines/runners/local-win-x64-01/2026-06-25/session-02/`,
    `docs/benchmarks/baselines/runners/local-win-x64-01/2026-06-25/history.json`,
    `docs/benchmarks/baselines/runners/local-win-x64-01/history.json`,
    `docs/benchmarks/baselines/index.md`, root 상태 문서.
  - 결과: raw report 6개, `summary.json`, `summary.md`를 생성하고,
    date-level `history.json`/`history.md`, runner root `history.json`/`history.md`를 재생성했다.
  - 비고: 2026-06-25 date root 는 session-count 2, hard-passed true, warning-count 0,
    comparison-compatible true 다. runner root 는 session-count 5, hard-passed true,
    warning-count 0, comparison-compatible true 다. explicit runner envelope 는 load p99 max 935.6 us,
    open-loop p99 max 1077.4 us 이다. 두 번째 date root 가 아직 2-session 이므로 gate 승격은 보류한다.
  - 검증: baseline suite pass, summary CLI source-report-count 6/hard-passed true/warning-count 0,
    date history CLI session-count 2/hard-passed true/warning-count 0,
    runner history CLI session-count 5/hard-passed true/warning-count 0.
    runner artifact local absolute path 검색 결과 없음. `Hps.Benchmarks.Tests` 67개 통과,
    `git diff --check` exit 0, restore asset 재생성 후 solution build 경고 0/오류 0,
    solution tests 269개 통과.

- [x] `local-win-x64-01/2026-06-25/session-01` explicit runner baseline 을 수집했다.
  - 범위: `docs/benchmarks/baselines/runners/local-win-x64-01/2026-06-25/session-01/`,
    `docs/benchmarks/baselines/runners/local-win-x64-01/2026-06-25/history.json`,
    `docs/benchmarks/baselines/runners/local-win-x64-01/history.json`,
    `docs/benchmarks/baselines/index.md`, root 상태 문서.
  - 결과: raw report 6개, `summary.json`, `summary.md`, date-level `history.json`/`history.md`,
    runner root `history.json`/`history.md`를 생성했다.
  - 비고: 2026-06-25 date root 는 session-count 1, hard-passed true, warning-count 0,
    comparison-compatible true 다. runner root 는 session-count 4, hard-passed true,
    warning-count 0, comparison-compatible true 다. explicit runner envelope 는 load p99 max 921.1 us,
    open-loop p99 max 1077.4 us 이다. 두 번째 date root 가 아직 1-session 이므로 gate 승격은 보류한다.
  - 검증: baseline suite pass, summary CLI source-report-count 6/hard-passed true/warning-count 0,
    date history CLI session-count 1/hard-passed true/warning-count 0,
    runner history CLI session-count 4/hard-passed true/warning-count 0.
    runner artifact local absolute path 검색 결과 없음. `Hps.Benchmarks.Tests` 67개 통과,
    `git diff --check` exit 0, restore asset 재생성 후 solution build 경고 0/오류 0,
    solution tests 269개 통과.

- [x] explicit runner 3-session 이후 Phase 4 다음 후보를 재평가했다.
  - 범위: `docs/superpowers/specs/2026-06-25-phase4-after-explicit-runner-reference-reassessment.md`,
    `DECISIONS.md`, root 상태 문서.
  - 결과: 다음 단위를 `local-win-x64-01/2026-06-25/session-01` explicit runner baseline 수집으로 정했다(D085).
  - 비고: 같은 runner 의 date root 가 아직 1개뿐이므로 CI/warning-as-failure 설계는 다음 date root 표본을 추가한 뒤 다시 평가한다.
  - 검증: `local-win-x64-01/2026-06-24/history.json`, `docs/benchmarks/baselines/index.md`,
    D082/D084, `.claude/review/`의 기존 benchmark 리뷰 의견을 대조했다.
    신규 spec placeholder 검색 결과 없음. `git diff --check` exit 0,
    solution build 경고 0/오류 0, solution tests 269개 통과.

- [x] explicit runner baseline 을 3-session reference 로 확장하고 문서 batch 를 완료했다.
  - 범위: `docs/benchmarks/baselines/runners/local-win-x64-01/2026-06-24/session-02/`,
    `docs/benchmarks/baselines/runners/local-win-x64-01/2026-06-24/session-03/`,
    `docs/benchmarks/baselines/runners/local-win-x64-01/2026-06-24/history.json`,
    `docs/benchmarks/baselines/runners/local-win-x64-01/2026-06-24/history.md`,
    `docs/benchmarks/baselines/index.md`, root 상태 문서.
  - 결과: `session-02`, `session-03` raw report 를 각각 6개씩 생성하고, 각 summary artifact 와
    3-session 기준 date-level history artifact 를 재생성했다.
  - 비고: history 는 `session-count=3`, `hard-passed=true`, `warning-count=0`,
    `comparison-compatible=true`, unknown runner 0, mismatch 0 이다.
    explicit runner envelope 는 load p99 max 870.7 us, open-loop p99 max 1051.5 us 이다.
    같은 runner 의 date root 가 아직 1개뿐이므로 D082 warning-as-failure 승격 조건에는 산입하지 않는다.
  - 검증: session-02/session-03 baseline suite pass, 각 summary CLI source-report-count 6/hard-passed true/warning-count 0,
    history CLI session-count 3/hard-passed true/warning-count 0.
    runner artifact local absolute path 검색 결과 없음. `Hps.Benchmarks.Tests` 67개 통과,
    `git diff --check` exit 0, solution build 경고 0/오류 0, solution tests 269개 통과.

- [x] 첫 explicit runner baseline 을 새 runner group 구조에 수집했다.
  - 범위: `docs/benchmarks/baselines/runners/local-win-x64-01/2026-06-24/session-01/`,
    `docs/benchmarks/baselines/runners/local-win-x64-01/2026-06-24/history.json`,
    `docs/benchmarks/baselines/runners/local-win-x64-01/2026-06-24/history.md`,
    `docs/benchmarks/baselines/index.md`, root 상태 문서.
  - 결과: raw report 6개, `summary.json`, `summary.md`, date-level `history.json`, `history.md`를 생성했다.
  - 비고: `runner-id=local-win-x64-01`, `runner-kind=local`, `comparison-compatible=true`, unknown runner 0, mismatch 0 이다.
    첫 explicit runner baseline 은 저장 구조 검증 표본이며, 아직 D082 warning-as-failure 승격 표본은 아니다.
  - 검증: baseline suite pass, summary CLI source-report-count 6/hard-passed true/warning-count 0,
    history CLI session-count 1/hard-passed true/warning-count 0.
    runner artifact local absolute path 검색 결과 없음. `Hps.Benchmarks.Tests` 67개 통과,
    `git diff --check` exit 0, solution build 경고 0/오류 0, solution tests 269개 통과.

- [x] explicit runner baseline 저장 구조와 수집 정책을 설계했다.
  - 범위: `docs/superpowers/specs/2026-06-24-explicit-runner-baseline-storage-policy-design.md`,
    `docs/benchmarks/baselines/index.md`, `DECISIONS.md`, `docs/agent-state/decisions/2026-06.md`, root 상태 문서.
  - 결과: 명시적 runner baseline 은 `docs/benchmarks/baselines/runners/<runner-id>/YYYY-MM-DD/session-NN/`
    구조에 저장하기로 했다(D084).
  - 비고: 현재 `BaselineHistoryReader`는 runner root 를 parent root 로 받아 바로 아래 `YYYY-MM-DD` directories 를 읽을 수 있다.
    기존 top-level date roots 는 legacy/local-unspecified baseline 으로 보존한다.
  - 검증: D079/D080/D082/D083과 `BaselineHistoryReader` directory 규칙 대조 완료.
    신규 설계/결정/index 문서 임시 표기 검색 결과 없음.
    `git diff --check` exit 0, solution build 경고 0/오류 0, solution tests 269개 통과.

- [x] D082 이후 Phase 4 다음 실행 후보를 재평가하고 단일 작업 단위를 선정했다.
  - 범위: `docs/superpowers/specs/2026-06-24-phase4-next-candidate-reassessment.md`,
    `DECISIONS.md`, `docs/agent-state/decisions/2026-06.md`, root 상태 문서.
  - 결과: 명시적 runner id baseline 을 기존 `2026-06-24/session-04`처럼 바로 추가하지 않고,
    explicit runner baseline 저장 구조와 수집 정책을 먼저 설계하기로 했다(D083).
  - 비고: `BaselineHistoryReader`는 현재 `YYYY-MM-DD` date root 와 `session-NN`만 읽으므로, 같은 date root 에
    `local-unspecified`와 explicit runner id session 을 섞으면 intentional comparison mismatch 가 된다.
  - 검증: D082/D079/D080 및 `BaselineHistoryReader` directory 규칙 대조 완료.
    신규 설계/결정 문서 임시 표기 검색 결과 없음.
    `git diff --check` exit 0, solution build 경고 0/오류 0, solution tests 269개 통과.

- [x] D082 latency envelope/gate 보류 설계 검토 의견을 반영했다.
  - 범위: `docs/superpowers/specs/2026-06-24-latency-envelope-and-gate-deferral-design.md`,
    `docs/benchmarks/baselines/index.md`, `DECISIONS.md`, `docs/agent-state/decisions/2026-06.md`, root 상태 문서.
  - 결과: 집계 방식은 세 session summary 의 `by-kind` aggregate 를 세션 간 max/min 으로 다시 집계한다고 명시했다.
    2026-06-24 `runner-id=local-unspecified` baseline 은 gate 승격 표본 count 에 산입하지 않고 reference 로만 쓴다고 명시했다.
    envelope 초과 기록은 자동 failure 나 schema field 가 아니라 수동 리뷰 메모라고 명시했다.
  - 비고: 검토서는 승인 수준이며 must-fix는 없었다.
  - 검증: D082 리뷰 finding 1/2와 info 3 반영 여부 대조 완료.
    신규 설계/결정/index 문서 임시 표기 검색 결과 없음.
    `git diff --check` exit 0, solution build 경고 0/오류 0, solution tests 269개 통과.

- [x] 2026-06-24 compatible baseline 3개를 근거로 latency envelope 재산정과 warning-as-failure/CI gate 보류 조건을 설계했다.
  - 범위: `docs/superpowers/specs/2026-06-24-latency-envelope-and-gate-deferral-design.md`,
    `docs/benchmarks/baselines/index.md`, `DECISIONS.md`, `docs/agent-state/decisions/2026-06.md`, root 상태 문서.
  - 결과: D082로 2026-06-24 compatible baseline 3개를 reference latency envelope 로 채택하되,
    hard latency gate, warning-as-failure, CI latency failure 는 계속 보류한다고 정리했다.
  - 비고: 현 envelope 는 load p99 max 1020.4 us, open-loop p99 max 1006.5 us 이므로 1 ms hard SLO 는 현 baseline 과 맞지 않는다.
  - 검증: 2026-06-24 history/session summary 수치 대조 완료.
    신규 설계/결정/index 문서 임시 표기 검색 결과 없음.
    `git diff --check` exit 0, solution build 경고 0/오류 0, solution tests 269개 통과.

- [x] 2026-06-24 문서 전용 작업 batch 규칙을 명시했다.
  - 범위: `AGENT_RULES.md`, `DECISIONS.md`, `CURRENT_PLAN.md`, `TODOS.md`, `CHANGELOG_AGENT.md`.
  - 결과: 구현/테스트/리팩터링은 계속 작은 기능 단위로 유지하고, 문서 전용 작업은 관련 설계/상태/결정/검토 문서를
    한 coherent documentation cycle 에서 같이 정렬하는 기준을 추가했다.
  - 비고: 문서 batch 에 코드/테스트 구현 변경을 섞지 않는 경계도 함께 기록했다.
  - 검증: 관련 root 문서 용어 대조, `git diff --check` exit 0, solution build 경고 0/오류 0,
    solution tests 269개 통과.

- [x] 2026-06-24 current-schema baseline session-03 을 추가했다.
  - 범위: `docs/benchmarks/baselines/2026-06-24/session-03/*.json`,
    `docs/benchmarks/baselines/2026-06-24/session-03/summary.md`,
    `docs/benchmarks/baselines/2026-06-24/history.json`,
    `docs/benchmarks/baselines/2026-06-24/history.md`,
    `docs/benchmarks/baselines/index.md`, root 상태 문서.
  - 결과: D079 runner identity/environment metadata 를 포함한 raw report 6개(load 3회/open-loop 3회)와
    D080 comparison field 를 포함한 summary/history artifact 를 추가했다.
  - 비고: 2026-06-24 history 는 session-count 3이며 `comparison-compatible=true`, unknown runner 0, mismatch 0 이다.
  - 검증: baseline suite pass, summary CLI source-report-count 6/hard-passed true/warning-count 0,
    history CLI session-count 3/hard-passed true/warning-count 0.
    `docs/benchmarks/baselines/2026-06-24` 아래 local absolute path 검색은 매칭 없음이다.
    `Hps.Benchmarks.Tests` 67개 통과, `git diff --check` exit 0, solution build 경고 0/오류 0,
    solution tests 269개 통과.

- [x] 2026-06-24 current-schema baseline session-02 를 추가했다.
  - 범위: `docs/benchmarks/baselines/2026-06-24/session-02/*.json`,
    `docs/benchmarks/baselines/2026-06-24/session-02/summary.md`,
    `docs/benchmarks/baselines/2026-06-24/history.json`,
    `docs/benchmarks/baselines/2026-06-24/history.md`,
    `docs/benchmarks/baselines/index.md`, root 상태 문서.
  - 결과: D079 runner identity/environment metadata 를 포함한 raw report 6개(load 3회/open-loop 3회)와
    D080 comparison field 를 포함한 summary/history artifact 를 추가했다.
  - 비고: 2026-06-24 history 는 session-count 2이며 `comparison-compatible=true`, unknown runner 0, mismatch 0 이다.
  - 검증: baseline suite pass, summary CLI source-report-count 6/hard-passed true/warning-count 0,
    history CLI session-count 2/hard-passed true/warning-count 0.
    `docs/benchmarks/baselines/2026-06-24` 아래 local absolute path 검색은 매칭 없음이다.
    `Hps.Benchmarks.Tests` 67개 통과, `git diff --check` exit 0, solution build 경고 0/오류 0,
    solution tests 269개 통과.

- [x] 2026-06-24 current-schema baseline session 을 추가했다.
  - 범위: `docs/benchmarks/baselines/2026-06-24/session-01/*.json`,
    `docs/benchmarks/baselines/2026-06-24/session-01/summary.md`,
    `docs/benchmarks/baselines/2026-06-24/history.json`,
    `docs/benchmarks/baselines/2026-06-24/history.md`,
    `docs/benchmarks/baselines/index.md`, root 상태 문서.
  - 결과: D079 runner identity/environment metadata 를 포함한 raw report 6개(load 3회/open-loop 3회)와
    D080 comparison field 를 포함한 summary/history artifact 를 추가했다.
  - 비고: 이번 session 의 summary/history comparison 은 `comparison-compatible=true`, unknown runner 0, mismatch 0 이다.
  - 검증: baseline suite pass, summary CLI source-report-count 6/hard-passed true/warning-count 0,
    history CLI session-count 1/hard-passed true/warning-count 0.
    `docs/benchmarks/baselines/2026-06-24` 아래 local absolute path 검색은 매칭 없음이다.
    `Hps.Benchmarks.Tests` 67개 통과, `git diff --check` exit 0, solution build 경고 0/오류 0,
    solution tests 269개 통과.

- [x] 2026-06-24 2026-06-18 baseline summary/history artifact 를 현재 schema 로 재생성했다.
  - 범위: `docs/benchmarks/baselines/2026-06-18/summary.json`,
    `docs/benchmarks/baselines/2026-06-18/summary.md`,
    `docs/benchmarks/baselines/2026-06-18/session-02/summary.json`,
    `docs/benchmarks/baselines/2026-06-18/session-02/summary.md`,
    `docs/benchmarks/baselines/2026-06-18/session-03/summary.json`,
    `docs/benchmarks/baselines/2026-06-18/session-03/summary.md`,
    `docs/benchmarks/baselines/2026-06-18/history.json`,
    `docs/benchmarks/baselines/2026-06-18/history.md`,
    `docs/benchmarks/baselines/index.md`, root 상태 문서.
  - 결과: root/session-02/session-03 summary artifact 가 D080 comparison field 를 포함하고,
    date-level history artifact 가 세 session 을 집계한다.
  - 추가 보정: `BaselineReportReader`가 `SourcePath`를 입력 directory 기준 상대 경로로 보존하게 해
    committed artifact 에 local workspace 절대 경로가 들어가지 않게 했다.
  - 비고: 기존 raw report 는 D079 이전 artifact 라서 comparison 은 `unknown-runner` mismatch 로 false 이며,
    이는 hard failure 나 warning 이 아니라 비교 가능성 신호다.
  - 검증: summary CLI 3회 모두 source-report-count 6, hard-passed true, warning-count 0.
    history CLI 1회는 session-count 3, hard-passed true, warning-count 0.
    relative source-path focused test 는 Red/Green 을 확인했고, artifact 절대 경로 검색은 매칭 없음이다.
    `Hps.Benchmarks.Tests` 67개 통과, `git diff --check` exit 0, solution build 경고 0/오류 0,
    solution tests 269개 통과.

- [x] 2026-06-24 benchmark writer metadata roundtrip test 를 보강했다.
  - 범위: `tests/Hps.Benchmarks.Tests/BaselineReportReaderWriterTests.cs`, root 상태 문서.
  - 결과: `TcpLoopbackReportWriter`가 쓴 raw report 를 `BaselineReportReader`로 다시 읽어 D079 runner/environment metadata 전체를 검증한다.
  - 비고: `os-architecture=Arm64`, `process-architecture=X64`를 의도적으로 다르게 둬 architecture field name drift 와 혼동을 잡는다.
  - Red: `TcpLoopbackReportWriter`의 `process-architecture` field 이름을 임시로 바꿨을 때 새 roundtrip test 가
    `Expected: "X64", Actual: "unknown"` assertion failure 로 실패함을 확인했다.
  - Green/검증: focused roundtrip test 1개 통과, `Hps.Benchmarks.Tests` 66개 통과, `git diff --check` exit 0,
    solution build 경고 0/오류 0, solution tests 268개 통과.

- [x] 2026-06-24 summary/history comparison signal 계획 리뷰 보강을 완료했다.
  - 범위: `.claude/review/2026-06-24-summary-history-comparison-signal-plan-review.md`,
    `docs/superpowers/plans/2026-06-24-summary-history-comparison-signal.md`, `DECISIONS.md`,
    `tests/Hps.Benchmarks.Tests/BaselineSummaryGeneratorTests.cs`,
    `tests/Hps.Benchmarks.Tests/BaselineSummaryMarkdownWriterTests.cs`, root 상태 문서.
  - 결과: Summary Markdown null-key/legacy unknown 경로와 partial unknown identity 판정을 테스트로 고정했다.
  - 비고: hard comparison identity field 중 하나라도 `unknown`이면 partial metadata 라도 `unknown-runner`로 본다.
  - Red: null-key guard 제거 mutation 에서 Markdown test 가 `NullReferenceException`으로 실패함을 확인했다.
    partial unknown predicate 약화 mutation 에서 generator test 가 `Assert.False()` failure 로 실패함을 확인했다.
  - Green/검증: focused 보강 tests 2개 통과, `Hps.Benchmarks.Tests` 65개 통과.

- [x] 2026-06-24 summary/history comparison signal Task 5를 구현했다.
  - 범위: `tests/Hps.Benchmarks/BaselineHistoryWriter.cs`,
    `tests/Hps.Benchmarks/BaselineHistoryMarkdownWriter.cs`,
    `tests/Hps.Benchmarks.Tests/BaselineHistoryGeneratorWriterTests.cs`,
    `tests/Hps.Benchmarks.Tests/BaselineHistoryProgramTests.cs`, root 상태 문서.
  - 결과: history JSON top-level/session entry 에 comparison field 를 출력하고,
    history Markdown 에 `## Comparison` section 을 출력한다.
  - 비고: comparison mismatch-only history 는 hard gate/warning-count 를 바꾸지 않고 Program exit code 0을 유지한다.
  - Red: JSON writer/Program tests 가 comparison field 부재로 `KeyNotFoundException`을 냄을 확인했다.
    Markdown writer test 는 `## Comparison` section 부재로 `Assert.Contains()` 실패함을 확인했다.
  - Green/검증: focused Task 5 tests 3개 통과, `Hps.Benchmarks.Tests` 63개 통과.

- [x] 2026-06-24 summary/history comparison signal Task 4를 구현했다.
  - 범위: `tests/Hps.Benchmarks/BaselineHistorySession.cs`, `tests/Hps.Benchmarks/BaselineHistory.cs`,
    `tests/Hps.Benchmarks/BaselineHistoryReader.cs`, `tests/Hps.Benchmarks/BaselineHistoryGenerator.cs`,
    `tests/Hps.Benchmarks.Tests/BaselineHistoryReaderTests.cs`,
    `tests/Hps.Benchmarks.Tests/BaselineHistoryGeneratorWriterTests.cs`, root 상태 문서.
  - 결과: history session/history model 이 comparison result 를 보존하고,
    reader 는 summary comparison field 와 legacy fallback 을 읽으며,
    generator 는 session comparison key 를 history-level compatibility 로 집계한다.
  - 비고: comparison mismatch 는 hard gate, failed-session-count, warning-count 를 바꾸지 않는 별도 result 로 유지한다.
  - Red: comparison property contract tests 2개가 `Assert.NotNull()` 실패함을 확인했다.
    reader/generator behavior tests 5개는 stub comparison 에서 `Assert.True()`/`Assert.Single()` 실패함을 확인했다.
  - Green/검증: focused history reader/generator tests 12개 통과.

- [x] 2026-06-24 summary/history comparison signal Task 3을 구현했다.
  - 범위: `tests/Hps.Benchmarks/BaselineSummaryWriter.cs`,
    `tests/Hps.Benchmarks/BaselineSummaryMarkdownWriter.cs`,
    `tests/Hps.Benchmarks.Tests/BaselineReportReaderWriterTests.cs`,
    `tests/Hps.Benchmarks.Tests/BaselineSummaryMarkdownWriterTests.cs`, root 상태 문서.
  - 결과: summary JSON top-level 에 comparison-compatible/key/mismatch field 를 쓰고,
    summary Markdown 에 `## Comparison` section 과 workload case table 을 출력한다.
  - 비고: output 은 Task 2에서 계산한 `BaselineSummary.Comparison`을 그대로 사용하며 writer 에서 재계산하지 않는다.
  - Red: JSON writer test 가 `comparison-compatible` field 부재로 `KeyNotFoundException`을 냄을 확인했다.
    Markdown writer test 는 `## Comparison` section 부재로 `Assert.Contains()` 실패함을 확인했다.
  - Green/검증: focused JSON writer test 1개 통과, focused Markdown writer tests 3개 통과,
    `Hps.Benchmarks.Tests` 53개 통과.

- [x] 2026-06-24 summary/history comparison signal Task 2를 구현했다.
  - 범위: `tests/Hps.Benchmarks/BaselineComparisonCase.cs`, `BaselineComparisonKey.cs`,
    `BaselineComparisonMismatch.cs`, `BaselineComparisonResult.cs`, `BaselineSummary.cs`, `BaselineSummaryGenerator.cs`,
    `tests/Hps.Benchmarks.Tests/BaselineSummaryGeneratorTests.cs`, root 상태 문서.
  - 결과: `BaselineSummary.Comparison`과 내부 comparison model 을 추가했고, summary generator 가 compatible 여부,
    key, unknown runner count, mismatch 목록을 계산한다.
  - 비고: `processor-count`는 D080대로 comparison key 에 넣지 않고, `load`/`open-loop`은 `result-name`별 case 로 분리한다.
  - Red: `BaselineSummary.Comparison` property 부재 contract test 가 `Assert.NotNull()` 실패함을 확인했다.
    compatible behavior test 는 stub comparison 에서 `Expected: True, Actual: False`로 실패함을 확인했다.
  - Green/검증: focused `BaselineSummaryGeneratorTests` 8개 통과, `Hps.Benchmarks.Tests` 51개 통과.

- [x] 2026-06-24 summary/history comparison signal Task 1을 구현했다.
  - 범위: `tests/Hps.Benchmarks/BaselineReport.cs`, `tests/Hps.Benchmarks/BaselineReportReader.cs`,
    `tests/Hps.Benchmarks.Tests/BaselineReportReaderWriterTests.cs`,
    `tests/Hps.Benchmarks.Tests/BaselineSummaryGeneratorTests.cs`,
    `tests/Hps.Benchmarks.Tests/BaselineSummaryMarkdownWriterTests.cs`, root 상태 문서.
  - 결과: `BaselineReport`가 raw report 의 `PayloadBytes`, `TargetRateHz`, `TargetDurationSeconds`를 보존하고,
    `BaselineReportReader`가 `payload-bytes`, `target-rate-hz`, `target-duration-seconds`를 읽는다.
  - 비고: direct `BaselineReport` helper 호출부에는 현재 benchmark 기본값 `4096`, `100.0`, `30`을 명시했다.
  - Red: payload/target property 부재 contract test 가 `Assert.NotNull()` 실패함을 확인했다.
    reader behavior test 는 `Expected: 4096, Actual: 0`으로 실패함을 확인했다.
  - Green/검증: focused `BaselineReportReaderWriterTests` 8개 통과, focused `BaselineSummary*` 6개 통과,
    `Hps.Benchmarks.Tests` 46개 통과.

- [x] 2026-06-24 summary/history comparison signal 구현 계획을 작성했다.
  - 범위: `docs/superpowers/specs/2026-06-23-summary-history-comparison-signal-design.md`,
    `tests/Hps.Benchmarks/BaselineReport*.cs`, `BaselineSummary*.cs`, `BaselineHistory*.cs`,
    관련 benchmark tests.
  - 결과: `docs/superpowers/plans/2026-06-24-summary-history-comparison-signal.md`에 5개 커밋 단위 구현 계획을 추가했다.
  - 작업 단위: Task 1 `BaselineReport` payload/target settings, Task 2 summary comparison model/generator,
    Task 3 summary JSON/Markdown output, Task 4 history reader/generator aggregate,
    Task 5 history JSON/Markdown output 과 CLI smoke.
  - 비고: 새 테스트에는 무엇을 검증하는지 한국어 주석을 남기고, comparison mismatch 는 hard gate/기존 `warning-count`/exit code 와
    분리한다는 요구를 계획에 명시했다.
  - 검증: D080 설계와 현재 source/test 구조를 대조해 touched files, Red/Green 경계, 커밋 경계를 확인했다.

- [x] 2026-06-23 summary/history comparison signal 설계를 완료했다.
  - 범위: `docs/superpowers/specs/2026-06-23-summary-history-comparison-signal-design.md`,
    D079 raw metadata, `BaselineReport`, `BaselineSummary*`, `BaselineHistory*`.
  - 결과: summary/history JSON에 `comparison-compatible`, `comparison-key`, `comparison-mismatch-count`,
    `comparison-mismatches`, `unknown-runner-count` 계열 additive field 를 두는 설계를 작성했다.
  - 결정: D080으로 comparison signal 은 hard gate, 기존 `warning-count`, CLI exit code 에 영향을 주지 않는
    non-failing compatibility artifact 로 둔다.
  - 비고: summary 안에서 `load`와 `open-loop` scenario 가 다를 수 있으므로, comparison key 는 단일 scenario 가 아니라
    `result-name`별 `cases` 배열로 표현한다.
  - 검증: current benchmark model/writer/reader 구조와 D079 설계를 대조했다.
    `git diff --check`, solution build 경고 0/오류 0, solution tests 246개 통과.

- [x] 2026-06-23 benchmark runner identity Task 1~3 구현 검토를 완료했다.
  - 범위: D079 설계, 구현 계획, `BenchmarkRunIdentity`, `TcpLoopbackRunResult`, `TcpLoopbackReportWriter`,
    `BaselineReport`, `BaselineReportReader`, 관련 focused tests.
  - 결과: 새 Blocker/Major finding 은 없다.
  - 비고: writer metadata field drift 를 더 강하게 잡는 roundtrip test 는 `P3_NICE` deferred backlog 로 남겼다.
  - 리뷰: `docs/agent-state/reviews/2026-06-23-benchmark-runner-identity-implementation-review.md`.
  - 검증: 코드/테스트/문서 대조를 수행했다. `git diff --check`, solution build 경고 0/오류 0, solution tests 246개 통과.

- [x] 2026-06-23 benchmark runner identity Task 3 raw report reader/legacy compatibility 를 구현했다.
  - 범위: `tests/Hps.Benchmarks/BaselineReport.cs`, `tests/Hps.Benchmarks/BaselineReportReader.cs`,
    `tests/Hps.Benchmarks.Tests/BaselineReportReaderWriterTests.cs`, root 상태 문서.
  - 결과: `BaselineReport`가 `BenchmarkRunIdentity`를 보존하고, `BaselineReportReader`가 신규 raw report metadata 를 읽는다.
  - 비고: metadata 가 없는 legacy raw report 는 crash 나 임의 추론 없이 `BenchmarkRunIdentity.Unknown`으로 보존한다.
  - Red: `BaselineReport.Identity` property 부재로 contract test 가 `Assert.NotNull()` 실패함을 확인했다.
    metadata 포함 raw report reader test 는 `Expected: tcp-loopback-saea-v1, Actual: unknown`으로 실패함을 확인했다.
  - Green/검증: focused `BaselineReportReaderWriterTests` 6개 통과, `Hps.Benchmarks.Tests` 44개 통과.
    `git diff --check`, solution build 경고 0/오류 0, solution tests 246개 통과.

- [x] 2026-06-23 benchmark runner identity Task 2 raw report writer metadata 를 구현했다.
  - 범위: `tests/Hps.Benchmarks/TcpLoopbackRunResult.cs`, `tests/Hps.Benchmarks/TcpLoopbackReportWriter.cs`,
    `tests/Hps.Benchmarks.Tests/BaselineReportReaderWriterTests.cs`, root 상태 문서.
  - 결과: `TcpLoopbackRunResult`가 `BenchmarkRunIdentity`를 보존하고, `TcpLoopbackReportWriter`가 raw report schema v1 top-level 에
    runner/environment metadata field 를 additive 로 기록한다.
  - 비고: 기존 runner 생성자는 identity optional parameter 로 호환성을 유지하며, 명시 identity 가 없으면 `CaptureDefault()`를 사용한다.
  - Red: writer metadata shape test 가 `benchmark-profile` 미기록으로 `Assert.True()` 실패함을 확인했다.
  - Green/검증: focused writer metadata test 1개 통과, `Hps.Benchmarks.Tests` 41개 통과.
    `git diff --check`, solution build 경고 0/오류 0, solution tests 243개 통과.

- [x] 2026-06-23 benchmark runner identity Task 1 model 을 구현했다.
  - 범위: `tests/Hps.Benchmarks/BenchmarkRunIdentity.cs`, `tests/Hps.Benchmarks.Tests/BenchmarkRunIdentityTests.cs`, root 상태 문서.
  - 결과: raw report metadata 의 공통 identity model 과 `CaptureDefault()`를 추가했다.
  - 비고: default runner id/kind 는 privacy 우선으로 `local-unspecified`/`local`이며,
    명시 override 는 `HPS_BENCHMARK_RUNNER_ID`, `HPS_BENCHMARK_RUNNER_KIND`만 사용한다.
  - Red: 타입 부재 contract test 1개 `Assert.NotNull()` 실패, behavior tests 2개가 `unknown` 반환으로 실패함을 확인했다.
  - Green/검증: focused `BenchmarkRunIdentityTests` 3개 통과, `Hps.Benchmarks.Tests` 40개 통과.
    `git diff --check`, solution build 경고 0/오류 0, solution tests 242개 통과.

- [x] 2026-06-23 benchmark runner identity 구현 계획을 작성했다.
  - 범위: D079 설계, benchmark raw report writer/reader/source model, 기존 benchmark test 패턴.
  - 결과: `docs/superpowers/plans/2026-06-23-benchmark-runner-identity.md`에 3개 커밋 단위 구현 계획을 추가했다.
  - 작업 단위: Task 1 `BenchmarkRunIdentity` model, Task 2 raw report writer metadata, Task 3 raw report reader/legacy compatibility.
  - 비고: summary/history comparison signal 은 raw metadata 원천 기록 뒤 별도 단위에서 다룬다.
  - 검증: 계획 self-review 로 D079 coverage, type consistency, commit boundary 를 확인했다.
    `git diff --check`, solution build 경고 0/오류 0, solution tests 239개 통과.

- [x] 2026-06-23 Phase 4 backlog 를 재평가하고 benchmark runner identity 를 다음 구현 후보로 설계했다.
  - 범위: baseline history 이후 남은 Phase 4 항목, D069/D070/D071/D078, benchmark raw report/summary/history source, baseline index.
  - 결과: CI workflow/warning-as-failure/latency hard gate 보다 runner identity/environment metadata 를 먼저 기록해야 한다고 판단했다.
    설계는 `docs/superpowers/specs/2026-06-23-benchmark-runner-identity-design.md`에 기록했고 D079로 결정했다.
  - 비고: schema 는 raw report v1 additive field 로 유지하고, host name/user name/IP address 는 자동 수집하지 않는다.
  - 검증: 관련 상태 문서, 결정 문서, benchmark writer/reader/source model 을 대조했다.
    `git diff --check`, solution build 경고 0/오류 0, solution tests 239개 통과.

- [x] 2026-06-23 baseline history report command 전체 구현 검토를 완료했다.
  - 범위: Task 1~4 parser/reader/generator/writer/Program wiring, tests, D078 설계 정합성.
  - 결과: 새 Blocker/Major finding 은 없고, `docs/agent-state/reviews/2026-06-23-baseline-history-command-implementation-review.md`에 검토 결과를 기록했다.
  - 비고: CLI optional Markdown path 오류 메시지 정밀화와 date root 직접 입력 Program smoke 는 비차단 후속으로 남겼다.
  - 검증: 실제 baseline root CLI smoke 로 session-count 3, hard-passed true, warning-count 0과 UTF-8 Markdown 출력을 확인했다.

- [x] 2026-06-23 baseline history report command Task 4 Program wiring/smoke 를 구현했다.
  - 범위: `tests/Hps.Benchmarks/Program.cs`, `tests/Hps.Benchmarks.Tests/BaselineHistoryProgramTests.cs`, root 상태 문서.
  - 결과: `Program.Main`이 `--summarize-baseline-history <baseline-root> --history <output-json> [--history-md <output-md>]`를
    실행해 history JSON/Markdown artifact 를 생성한다.
  - 비고: warning-only history 는 success exit code 를 유지하고, failed session 이 있으면 failed-run exit code 를 반환한다.
  - Red: focused Program tests 3개가 구현 전 usage error exit code 2 반환으로 실패함을 확인했다.
  - Green/검증: focused Program tests 3개 통과, 실제 baseline root CLI smoke 는 session-count 3, hard-passed true, warning-count 0을 출력했다.
    `git diff --check`, solution build 경고 0/오류 0, solution tests 239개 통과.

- [x] 2026-06-23 baseline history report command Task 3 history aggregate/writer 를 구현했다.
  - 범위: `tests/Hps.Benchmarks/BaselineHistory.cs`, `BaselineHistoryGenerator.cs`, `BaselineHistoryWriter.cs`,
    `BaselineHistoryMarkdownWriter.cs`, `tests/Hps.Benchmarks.Tests/BaselineHistoryGeneratorWriterTests.cs`, root 상태 문서.
  - 결과: session 목록을 history aggregate 로 변환하고, stable JSON schema 와 Markdown 보조 artifact 를 생성한다.
  - 비고: `hard-passed`는 session flag AND, 실패 카운터는 `failed-session-count`, p99 누락은 JSON `null`/Markdown `-`로 표현한다.
  - Red: reflection contract test 실패 1개, behavior tests 5개가 aggregate/writer stub 에서 실패함을 확인했다.
  - Green/검증: focused generator/writer tests 5개 통과, `git diff --check`, solution build 경고 0/오류 0, solution tests 236개 통과.

- [x] 2026-06-23 baseline history report command Task 2 history domain/reader 를 구현했다.
  - 범위: `tests/Hps.Benchmarks/BaselineHistorySession.cs`, `BaselineHistoryReader.cs`,
    `tests/Hps.Benchmarks.Tests/BaselineHistoryReaderTests.cs`, root 상태 문서.
  - 결과: date root 와 parent baseline root 를 bounded discovery 로 읽고, legacy root `summary.json`과
    `session-NN/summary.json`을 `BaselineHistorySession` 목록으로 변환한다.
  - 비고: load/open-loop p99 가 없으면 `null`로 보존하고, HWM 은 없으면 0으로 둔다. summary 가 하나도 없으면 실패한다.
  - Red: reflection contract test 실패 1개, behavior tests 4개가 stub `NotSupportedException`으로 실패함을 확인했다.
  - Green/검증: focused reader tests 4개 통과, `git diff --check`, solution build 경고 0/오류 0, solution tests 231개 통과.

- [x] 2026-06-23 baseline history report command Task 1 parser contract 를 구현했다.
  - 범위: `tests/Hps.Benchmarks/BenchmarkCommand.cs`, `BenchmarkCommandLine.cs`, `BenchmarkCommandParser.cs`, `Program.cs`,
    `tests/Hps.Benchmarks.Tests/BenchmarkCommandParserTests.cs`, root 상태 문서.
  - 결과: `--summarize-baseline-history <baseline-root> --history <output-json> [--history-md <output-md>]` parser contract 를 추가했다.
    `--report` 혼용은 usage error 로 막고, 실행 wiring 은 계획대로 Task 4에 남겼다.
  - Red: focused parser tests 에서 history command 테스트 5개가 실패함을 확인했다.
  - Green/검증: focused parser tests 15개 통과, `git diff --check`, solution build 경고 0/오류 0, solution tests 227개 통과.

- [x] 2026-06-23 baseline history report command 구현 계획 리뷰 보정을 완료했다.
  - 범위: `.claude/review/2026-06-23-baseline-history-report-command-review.md`,
    baseline history command 설계/구현 계획, D078 결정 문서, root 상태 문서.
  - 결과: history `hard-passed` 기준을 session `hard-passed` AND 로 명시했고, root 실패 카운터를
    `failed-session-count`로 고정했으며, 누락 p99 는 JSON `null`/Markdown `-`로 표현하도록 계획을 보정했다.
  - 다음: Task 1(parser contract) 구현부터 진행한다.

- [x] 2026-06-23 baseline history report command 구현 계획을 작성했다.
  - 범위: D078 설계, baseline history 설계 리뷰, `tests/Hps.Benchmarks` parser/source, summary reader/writer/test 패턴.
  - 결과: `docs/superpowers/plans/2026-06-23-baseline-history-report-command.md`에 4개 커밋 단위 구현 계획을 추가했다.
  - 작업 단위: Task 1 parser contract, Task 2 history domain/reader, Task 3 aggregate/writer, Task 4 Program wiring/smoke.
  - 비고: Task 2/3은 새 타입 도입 시 컴파일 실패 Red 를 피하기 위해 reflection contract Red → stub → behavior Red 순서를 명시했다.
  - 검증: 계획 self-review 로 spec coverage, placeholder scan, type consistency, commit boundary 를 확인했다.

- [x] 2026-06-23 baseline history report command 설계 리뷰를 완료했다.
  - 범위: `docs/superpowers/specs/2026-06-23-baseline-history-report-command-design.md`,
    `tests/Hps.Benchmarks/`, `tests/Hps.Benchmarks.Tests/`, `docs/benchmarks/baselines/index.md`, 결정/상태 문서.
  - 결과: enum 이름 모호성은 `BenchmarkCommand.SummarizeBaselineHistory`로 고정했고, parent baseline root/date root 입력 discovery 규칙을 분리했다.
  - 결정: D078로 history command 를 provider-independent aggregate artifact 로 두고 warning 은 soft signal 로 유지한다고 기록했다.
  - 리뷰: `docs/agent-state/reviews/2026-06-23-baseline-history-report-command-design-review.md`.
  - 검증: benchmark CLI/parser/source, summary writer/generator, baseline artifact 구조를 대조했다.

- [x] 2026-06-23 Phase 4 backlog 를 재평가하고 baseline history report command 를 설계했다.
  - 범위: `CURRENT_PLAN.md`, `TODOS.md`, `DECISIONS.md`, baseline 관련 specs/plans/review, benchmark CLI/source 구조.
  - 결과: CI workflow/warning-as-failure 는 아직 보류하고, 여러 session `summary.json`을 읽어 `history.json`과 선택적 `history.md`를 쓰는
    provider-independent command 를 다음 구현 후보로 좁혔다.
  - 설계: `docs/superpowers/specs/2026-06-23-baseline-history-report-command-design.md`.
  - 검증: `PLAN.md`, `CURRENT_PLAN.md`, `TODOS.md`, `DECISIONS.md`, baseline specs/plans/review,
    `tests/Hps.Benchmarks` CLI/parser/summary source 를 대조했다.

- [x] 2026-06-23 UDP lease sweep registry race guard 리뷰를 완료했다.
  - 범위: `a817c6e`, `src/Hps.Broker/BrokerUdpDatagramHandler.cs`,
    `tests/Hps.Broker.Tests/BrokerUdpDatagramHandlerTests.cs`, D077 관련 문서, root 상태 문서.
  - 결과: handler gate 직렬화는 sweep/re-register stale cleanup race 를 닫고, `PUBLISH` fan-out 을 lock 밖에 둔 범위도 D077과 정합했다.
  - 비고: race regression test 의 250ms scheduling window 는 비차단 Minor 관찰로 남겼다. fixed path green 판단은 해당 반환값에 의존하지 않는다.
  - 검증: `git show`/`rg`/line review 로 코드·테스트·문서 정합성을 대조했다.

- [x] 2026-06-23 UDP lease sweep registry cleanup stale snapshot race 를 막았다.
  - 범위: `src/Hps.Broker/BrokerUdpDatagramHandler.cs`, `tests/Hps.Broker.Tests/BrokerUdpDatagramHandlerTests.cs`,
    `DECISIONS.md`, `docs/agent-state/decisions/2026-06.md`, root 상태 문서.
  - 결과: UDP receive command/endpoint-close/sweep state mutation 을 handler gate 로 직렬화해, sweep expired snapshot 이후
    같은 stable target 이 재등록되는 경우 stale registry cleanup 이 새 online 상태를 disconnected 로 덮지 못하게 했다.
  - 비고: `PUBLISH` fan-out 은 lock 밖에서 유지해 transport send path 를 handler gate 에 묶지 않는다(D077).
  - 검증: focused race test Red assertion failure 1개 확인(`Assert.True()` failure), focused race test 통과,
    focused UDP handler tests 17개 통과, Broker tests 73개 통과.
    `git diff --check`, solution build 경고 0/오류 0, solution tests 222개 통과.

- [x] 2026-06-23 UDP stable identity F1/F2 수정분 리뷰를 완료했다.
  - 범위: `b85220f`, `8749c64`, `src/Hps.Broker/BrokerUdpDatagramHandler.cs`,
    `src/Hps.Broker/UdpRemoteLeaseTracker.cs`, `src/Hps.Server/BrokerServer.cs`, root 상태 문서.
  - 결과: F2 invalid identity datagram isolation 은 적절하지만, F1 lease sweep registry cleanup 에 stale snapshot race 가 남아 있음을 확인했다.
  - 다음: 위 `P0_NOW` 항목으로 다음 구현 단위를 분리했다.
  - 검증: `rg` 기반 코드 경계 대조와 리뷰 문서 작성. `git diff --check`, solution build 경고 0/오류 0,
    solution tests 221개 통과.

- [x] 2026-06-23 UDP invalid stable identity datagram isolation 을 구현했다.
  - 범위: `src/Hps.Broker/BrokerUdpDatagramHandler.cs`, `tests/Hps.Broker.Tests/BrokerUdpDatagramHandlerTests.cs`, root 상태 문서.
  - 결과: UDP `REGISTER`/`UNREGISTER` identity token 이 decoder 를 통과한 뒤 registry validation 에서 거부될 값이어도
    handler 밖으로 예외가 전파되지 않고 해당 datagram 만 drop 된다.
  - 비고: Protocol decoder 전체 whitespace grammar 는 이번 범위에서 바꾸지 않았다. UDP handler boundary 에서 stable identity token 을
    비예외 방식으로 먼저 검사해 shared endpoint close 를 막는다.
  - 검증: focused Red assertion failure 2개 확인(`Assert.Null()` failure), focused invalid identity tests 2개 통과,
    focused UDP handler tests 16개 통과. `git diff --check`, solution build 경고 0/오류 0, solution tests 221개 통과.

- [x] 2026-06-23 UDP stable identity lease sweep registry cleanup 을 구현했다.
  - 범위: `src/Hps.Broker/BrokerUdpDatagramHandler.cs`, `src/Hps.Broker/UdpRemoteLeaseTracker.cs`,
    `tests/Hps.Broker.Tests/BrokerUdpDatagramHandlerTests.cs`, root 상태 문서.
  - 결과: UDP lease sweep 으로 만료된 stable remote target 이 routing table 뿐 아니라
    `SubscriberRegistry`에서도 disconnected 상태가 되어 retention sweep 대상이 된다.
  - 비고: `UdpRemoteLeaseTracker.SweepExpired(...)`의 기존 반환값은 routing table 제거 수로 유지하고,
    registry cleanup 용 expired target snapshot 은 선택적 side-channel 로 분리했다.
  - 검증: focused Red assertion failure 1개 확인(`Expected: 1, Actual: 0`), focused UDP handler tests 14개 통과.
    `git diff --check`, solution build 경고 0/오류 0, solution tests 219개 통과.

- [x] 2026-06-23 Stable subscriber identity 구현 교차검증을 완료했다.
  - 범위: D075/D076 설계, Protocol/Broker/Server 구현, stable identity 관련 tests, root 상태 문서.
  - 결과: 구현 방향은 타당하지만 UDP 경계 must-fix 2건을 발견했다.
    상세는 `docs/agent-state/reviews/2026-06-23-stable-subscriber-identity-cross-check.md`에 기록했다.
  - 검증: `rg`와 줄 번호 기반 소스/테스트 대조를 수행했다.
    `git diff --check`, solution build 경고 0/오류 0, solution tests 218개 통과.

- [x] 2026-06-22 Stable subscriber identity UDP loopback coverage 를 추가했다.
  - 범위: `tests/Hps.Server.Tests/BrokerServerTests.cs`, root 상태 문서.
  - 결과: 실제 `BrokerServer` + `SaeaTransport` UDP datagram loopback 에서 stable identity same-id remote rebind 가
    publish fan-out target 을 새 remote 로 옮김을 검증한다.
    새 remote 는 `REGISTER`만 보내고 `SUBSCRIBE`를 반복하지 않아 retained topic set 복구까지 확인한다.
  - 비고: UDP는 TCP처럼 old remote 를 close 할 수 없으므로, 이 테스트는 routing table 에서 old remote 만 제거되고
    새 remote 로 metadata 가 재바인딩되는 정책을 실제 datagram 경로로 고정한다.
  - 검증: focused stable UDP loopback test 1개 통과.
    `git diff --check`, solution build 경고 0/오류 0, solution tests 218개 통과.

- [x] 2026-06-22 Stable subscriber identity TCP loopback coverage 를 추가했다.
  - 범위: `tests/Hps.Server.Tests/BrokerServerTests.cs`, root 상태 문서.
  - 결과: 실제 `BrokerServer` + `SaeaTransport` TCP loopback 에서 stable identity reconnect/rebind 가 동작함을 검증한다.
    새 socket 은 `REGISTER`만 보내고 `SUBSCRIBE`를 반복하지 않아 retained topic set 복구까지 확인한다.
  - 비고: old TCP target close 는 Windows loopback 에서 FIN 또는 reset 으로 관측될 수 있어 두 경우를 모두 close 완료로 본다.
  - 검증: focused stable TCP loopback test 1개 통과.
    `git diff --check`, solution build 경고 0/오류 0, solution tests 217개 통과.

- [x] 2026-06-22 Stable subscriber identity UDP late REGISTER lease cleanup 을 구현했다.
  - 범위: `src/Hps.Broker/UdpRemoteLeaseTracker.cs`, `src/Hps.Broker/BrokerUdpDatagramHandler.cs`,
    `tests/Hps.Broker.Tests/BrokerUdpDatagramHandlerTests.cs`, stable identity 설계 문서, root 상태 문서.
  - 결과: UDP remote 가 `SUBSCRIBE` 후 `REGISTER`하면 routing table 뿐 아니라 optional lease tracker 의 pre-register
    runtime topic metadata 도 제거된다.
  - 비고: `REGISTER` 성공 후 같은 remote 의 lease metadata 는 registry rebound topic set 으로 교체하고,
    stable topic 이 없으면 lease 를 남기지 않는다.
  - 검증: focused UDP handler Red assertion failure 1개 확인, focused tests 13개 통과.
    `git diff --check`, solution build 경고 0/오류 0, solution tests 216개 통과.

- [x] 2026-06-22 Stable subscriber identity late REGISTER cleanup 을 구현했다.
  - 범위: `src/Hps.Broker/SubscriberRegistry.cs`, `tests/Hps.Broker.Tests/SubscriberRegistryTests.cs`,
    stable identity 설계 문서, 결정/상태 문서.
  - 결과: `SUBSCRIBE` 후 `REGISTER` 순서에서 identity metadata 에 없는 runtime 구독을 `REGISTER` 시점에 제거해,
    close cleanup 이후 stale target 이 routing table 에 남지 않게 했다.
  - 결정: D076으로 late `REGISTER`는 기존 runtime 구독을 stable identity metadata 로 자동 이관하지 않는다고 기록했다.
  - 검증: focused registry Red assertion failure 1개 확인, focused tests 10개 통과.
    `git diff --check`, solution build 경고 0/오류 0, solution tests 215개 통과.

- [x] 2026-06-22 Stable subscriber identity BrokerServer opt-in wiring 을 구현했다.
  - 범위: `src/Hps.Server/BrokerServerOptions.cs`, `src/Hps.Server/BrokerServer.cs`,
    `tests/Hps.Server.Tests/BrokerServerOptionsTests.cs`, `tests/Hps.Server.Tests/BrokerServerTests.cs`, root 상태 문서.
  - 결과: stable identity public options/factory/with method, shared `SubscriberRegistry` TCP/UDP handler 주입,
    retention sweep timer 생성/중복 방지/StopAsync dispose 를 연결했다.
  - 검증: stable identity Server/Options Red assertion failure 7개 확인, focused tests 7개 통과.
    `git diff --check`, solution build 경고 0/오류 0, solution tests 214개 통과.

- [x] 2026-06-22 Stable subscriber identity UDP handler wiring 을 구현했다.
  - 범위: `src/Hps.Broker/BrokerUdpDatagramHandler.cs`, `src/Hps.Broker/UdpRemoteLeaseTracker.cs`,
    `tests/Hps.Broker.Tests/BrokerUdpDatagramHandlerTests.cs`, root 상태 문서.
  - 결과: optional registry internal constructor, UDP `REGISTER`/`UNREGISTER`, registered remote subscribe/unsubscribe,
    same-id remote rebind, duplicate target datagram-drop, endpoint close retention 을 연결했다.
  - 검증: internal constructor 부재 Red assertion failure 4개 확인, focused UDP handler tests 12개 통과.

- [x] 2026-06-22 Stable subscriber identity TCP handler wiring 을 구현했다.
  - 범위: `src/Hps.Broker/BrokerTcpFrameHandler.cs`, `tests/Hps.Broker.Tests/BrokerTcpFrameHandlerTests.cs`, root 상태 문서.
  - 결과: optional registry/time provider internal constructor, TCP `REGISTER`/`UNREGISTER`, registered target subscribe/unsubscribe,
    same-id reconnect rebind, duplicate target reject/close, connection close cleanup 을 연결했다.
  - 검증: internal constructor 부재 Red assertion failure 4개 확인, focused TCP handler tests 11개 통과.

- [x] 2026-06-22 Stable subscriber identity pure registry 를 구현했다.
  - 범위: `src/Hps.Broker/SubscriberIdentity.cs`, `src/Hps.Broker/SubscriberRegistrationResult.cs`,
    `src/Hps.Broker/SubscriberRegistry.cs`, `tests/Hps.Broker.Tests/SubscriberIdentityTests.cs`,
    `tests/Hps.Broker.Tests/SubscriberRegistryTests.cs`, root 상태 문서.
  - 결과: identity token validation, identity별 topic metadata, same-id rebind, duplicate target conflict,
    disconnect retention, explicit unregister, disconnected sweep, UDP endpoint cleanup 을 pure model 로 추가했다.
  - 검증: reflection contract Red assertion failure 2개, behavior Red assertion failure 10개 확인,
    focused broker identity/registry tests 15개 통과.

- [x] 2026-06-22 Stable subscriber identity protocol decode 를 구현했다.
  - 범위: `src/Hps.Protocol/TcpCommandKind.cs`, `src/Hps.Protocol/TcpCommandDecoder.cs`,
    `tests/Hps.Protocol.Tests/TcpCommandDecoderTests.cs`, root 상태 문서.
  - 결과: `REGISTER <subscriber-id>`와 `UNREGISTER <subscriber-id>`를 token-only command 로 decode 한다.
  - 검증: Red assertion failure 9개 확인, focused protocol tests 24개 통과.

- [x] 2026-06-22 Stable subscriber identity 구현 계획을 작성했다.
  - 범위: `docs/superpowers/plans/2026-06-22-stable-subscriber-identity.md`, root 상태 문서.
  - 결과: D075 설계를 protocol decode, pure registry, TCP handler, UDP handler, Server opt-in wiring 의 5개 커밋 단위로 나눴다.
  - 검증: 계획 self-review 로 spec coverage, placeholder, type consistency 를 확인했다.
    `git diff --check`, solution build/test 로 문서 변경이 빌드 상태를 깨지 않음을 확인한다.

- [x] 2026-06-22 Stable subscriber identity / reconnect rebinding 정책을 설계했다.
  - 범위: `docs/superpowers/specs/2026-06-22-stable-subscriber-identity-reconnect-policy-design.md`,
    `DECISIONS.md`, root 상태 문서.
  - 결과: 기본 runtime target subscription 은 유지하고, 후속 stable identity 는 opt-in `REGISTER <subscriber-id>` 기반 Broker registry 로 설계했다.
  - 결정: 같은 id 재등록은 새 runtime target 이 이기며, disconnected 동안 payload 는 저장하지 않는다. `EndpointId`는 계속 diagnostics id 로 둔다.
  - 검증: 기존 endpoint identity policy, D058/D059/D060, 실제 Broker routing/handler/decoder 구조와 대조했다.
    `git diff --check`, solution build/test 로 문서 변경이 빌드 상태를 깨지 않음을 확인한다.

- [x] 2026-06-22 BrokerServer UDP lease sweep host timer/public settings 를 구현했다.
  - 범위: `src/Hps.Server/BrokerServer.cs`, `src/Hps.Broker/Properties/AssemblyInfo.cs`,
    `tests/Hps.Server.Tests/BrokerServerTests.cs`, root 상태 문서.
  - 결과: `BrokerServerOptions` enabled 설정을 `BrokerUdpDatagramHandler`에 연결하고,
    `StartUdpAsync` 성공 후 sweep timer 를 생성하며 `StopAsync`/start 실패 cleanup 에서 dispose 한다.
  - 비고: 기본 생성자는 options 생성자로 위임해 disabled 기본 동작과 enabled host timer 경로가 같은 초기화 흐름을 사용한다.
  - 검증: Red assertion failure 2개 확인, focused tests 2개 통과, 생성자 reflection 제거 후 focused tests 2개 통과,
    solution build 경고 0/오류 0, solution tests 175개 통과.

- [x] 2026-06-22 BrokerServerOptions public 설정 타입을 구현했다.
  - 범위: `src/Hps.Server/BrokerServerOptions.cs`, `tests/Hps.Server.Tests/BrokerServerOptionsTests.cs`, root 상태 문서.
  - 결과: 기본 disabled options, 양수 timeout/interval 검증, explicit time provider 저장을 추가했다.
  - 검증: Red assertion failure 3개 확인, focused tests 3개 통과, reflection 제거 후 focused tests 3개 통과,
    solution build 경고 0/오류 0, solution tests 173개 통과.

- [x] 2026-06-22 BrokerServer UDP lease host timer 설계를 작성했다.
  - 범위: `docs/superpowers/specs/2026-06-22-broker-server-udp-lease-host-timer-design.md`, `DECISIONS.md`, root 상태 문서.
  - 결과: `BrokerServerOptions` public 설정, 기본 disabled, explicit timeout/interval, `TimeProvider.CreateTimer`,
    `Hps.Broker` friend assembly 경계를 D074로 확정했다.
  - 검증: 설계 self-review, `git diff --check`, solution build/test.

- [x] 2026-06-22 UDP lease tracker handler wiring 을 구현했다.
  - 범위: `src/Hps.Broker/BrokerUdpDatagramHandler.cs`, `tests/Hps.Broker.Tests/BrokerUdpDatagramHandlerTests.cs`, root 상태 문서.
  - 결과: UDP SUBSCRIBE/UNSUBSCRIBE/PUBLISH/endpoint-close activity 가 `UdpRemoteLeaseTracker`로 연결되고, handler 내부 sweep entry point 가 생겼다.
  - 비고: public constructor 는 disabled options 를 사용해 기존 기본 동작을 보존하고, internal constructor 로 후속 host/test wiring 이 options/time provider 를 주입한다.
  - 검증: Red assertion failure 2개 확인, focused handler tests 8개 통과, reflection 제거 후 focused handler tests 8개 통과, solution build 경고 0/오류 0, solution tests 170개 통과.

- [x] 2026-06-22 UDP remote lease pure sweep 을 구현했다.
  - 범위: `src/Hps.Broker/UdpRemoteLeaseTracker.cs`, `tests/Hps.Broker.Tests/UdpRemoteLeaseTrackerTests.cs`, root 상태 문서.
  - 결과: `SweepExpired(DateTimeOffset)`가 idle timeout 을 초과한 UDP remote target 을 모든 topic 에서 제거한다.
  - 비고: plan 예시의 survivor remote setup 은 같은 시점 구독이면 함께 만료되므로 survivor를 늦게 구독하도록 테스트를 보정했다.
  - 검증: Red assertion failure 3개 확인, focused tests 8개 통과, reflection 제거 후 focused tests 8개 통과, solution build 경고 0/오류 0, solution tests 168개 통과.

- [x] 2026-06-22 UDP remote lease tracker activity 를 구현했다.
  - 범위: `src/Hps.Broker/UdpRemoteLeaseTracker.cs`, `tests/Hps.Broker.Tests/UdpRemoteLeaseTrackerTests.cs`, root 상태 문서.
  - 결과: disabled options 에서는 기존 subscription 동작만 수행하고, enabled options 에서는 UDP remote target 별 lease 를 생성/갱신/제거한다.
  - 비고: 계획서의 compile-failure Red는 AGENTS의 assertion-failure Red 규칙에 맞춰 reflection 기반 타입 부재 assertion 실패로 보정했다.
  - 검증: Red assertion failure 5개 확인, focused tests 5개 통과, reflection 제거 후 focused tests 5개 통과, solution build 경고 0/오류 0, solution tests 165개 통과.

- [x] 2026-06-22 UDP lease options 를 구현했다.
  - 범위: `src/Hps.Broker/UdpLeaseOptions.cs`, `src/Hps.Broker/Properties/AssemblyInfo.cs`, `tests/Hps.Broker.Tests/UdpLeaseOptionsTests.cs`, 구현 계획/상태 문서.
  - 결과: 기본 비활성 options, 양수 timeout/interval 검증, 테스트 assembly internal 접근 경계를 추가했다.
  - 비고: 계획서의 `Enabled(...)` factory 는 `Enabled` property 와 C# 멤버 이름이 충돌해 `CreateEnabled(...)`로 정정했다.
  - 검증: Red assertion failure 확인, focused tests 3개 통과, solution build 경고 0/오류 0, solution tests 160개 통과.

- [x] 2026-06-22 UDP optional lease sweep 구현 계획을 작성했다.
  - 범위: `docs/superpowers/plans/2026-06-22-udp-optional-lease-sweep.md`, root 상태 문서.
  - 결과: D073 설계를 4개 커밋 단위로 나누고 각 단위의 Red-Green 검증 경로, touched files, produced interfaces 를 명시했다.
  - 검증: 계획 self-review 완료, `git diff --check` 통과, solution build 경고 0/오류 0, solution tests 157개 통과.

- [x] 2026-06-22 UDP optional lease tracker / sweep owner 를 설계했다.
  - 범위: `docs/superpowers/specs/2026-06-22-udp-optional-lease-sweep-design.md`, root 상태 문서.
  - 결과: lease/sweep owner 를 Broker 소유·Server 트리거로, 설정을 내부 options(기본 비활성)로, 시간 소스를 `TimeProvider` 로 확정하고 sweep 의 `UnsubscribeAll(IUdpEndpoint, EndPoint)` 사용 방식을 D073으로 못 박았다.
  - 검증: `git diff --check` 통과, solution build 경고 0/오류 0, solution tests 157개 통과.

- [x] 2026-06-22 UDP remote-wide unsubscribe primitive 를 구현했다.
  - 범위: `src/Hps.Broker/SubscriptionTable.cs`, `tests/Hps.Broker.Tests/BrokerRoutingTests.cs`, root 상태 문서.
  - 결과: `(IUdpEndpoint, EndPoint)` 조합을 모든 topic 에서 제거하면서 같은 endpoint 의 다른 remote, 다른 endpoint 의 같은 remote, TCP subscriber 를 보존한다.
  - 검증: focused Red/Green/Refactor 완료, solution build 경고 0/오류 0, solution tests 157개 통과.

- [x] 2026-06-19 UDP stale remote idle expiry 를 설계했다.
  - 범위: `docs/superpowers/specs/2026-06-19-udp-stale-remote-idle-expiry-design.md`, root 상태 문서.
  - 결과: cleanup owner 를 Broker/Server 로 두고 기본 idle expiry 는 비활성화하며, 다음 구현을 remote-wide unsubscribe primitive 로 좁혔다(D072).
  - 검증: `git diff --check` 통과, solution build 경고 0/오류 0, solution tests 156개 통과.

- [x] 2026-06-18 baseline history index 를 추가했다.
  - 범위: `docs/benchmarks/baselines/index.md`, `docs/superpowers/specs/2026-06-18-baseline-report-history-warning-policy-design.md`, root 상태 문서.
  - 결과: 2026-06-18 root/session-02/session-03 summary artifact, hard/warning 상태, p99/HWM 대표값을 전역 entry point 에 연결하고 D071을 확정했다.
  - 검증: `git diff --check` 통과, solution build 경고 0/오류 0, solution tests 156개 통과.

- [x] 2026-06-18 baseline report history/warning 정책 설계를 작성했다.
  - 범위: `docs/superpowers/specs/2026-06-18-baseline-report-history-warning-policy-design.md`, root 상태 문서.
  - 결과: baseline session directory 를 history 단위로 보고, raw JSON/summary JSON/summary Markdown 역할을 분리하며, warning-as-failure 와 latency hard gate 는 보류하는 정책을 제안했다.
  - 검증: `git diff --check` 통과, solution build 경고 0/오류 0, solution tests 156개 통과.

- [x] 2026-06-18 baseline summary Markdown artifact 를 생성했다.
  - 범위: `docs/benchmarks/baselines/2026-06-18/**/summary.md`, `local-latency-baseline.md`, root 상태 문서.
  - 검증: CLI 3회 exit-code 0, source report count 6, hard-passed true, warning count 0. build 0/0, solution tests 156개 통과, `git diff --check` 통과.

- [x] baseline summary Markdown CLI 선택 출력을 연결했다.
  - 범위: `tests/Hps.Benchmarks/BenchmarkCommandLine.cs`, `BenchmarkCommandParser.cs`, `Program.cs`, parser tests, root 상태 문서.
  - 검증: parser/CLI Red-Green, focused benchmark tests 20개 통과, solution tests 156개 통과.

- [x] baseline summary Markdown writer 를 구현했다.
  - 범위: `BaselineSummaryMarkdownWriter`, writer tests, root 상태 문서.
  - 검증: writer Red-Green 후 focused tests 통과.

- [x] 2026-06-18 baseline summary JSON artifact 를 생성했다.
  - 범위: baseline root/session-02/session-03 `summary.json`, `local-latency-baseline.md`, root 상태 문서.
  - 검증: 세 summary 모두 source report count 6, hard-passed true, warning 0.

- [x] 반복 baseline summary artifact 와 soft warning 산출을 구현했다.
  - 범위: `tests/Hps.Benchmarks/`, `tests/Hps.Benchmarks.Tests/`, D070 spec/plan/state docs.
  - 검증: root/session-02/session-03 CLI smoke 통과, solution tests 통과.

- [x] 3개 반복 baseline session 기반 latency/CI 정책을 D070으로 정리했다.
  - 결과: p50/p99 hard threshold 는 보류하고 summary/soft warning 을 먼저 만든다.

- [x] State document compaction 을 수행했다.
  - 범위: `CURRENT_PLAN.md`, `TODOS.md`, `CHANGELOG_AGENT.md`, `DECISIONS.md`, `docs/agent-state/` archive.
  - 결과: root 상태 파일은 현재 진입점만 남기고 상세 이력은 archive 로 이동했다.
  - 검증: `git diff --check` 통과, solution build 경고 0/오류 0, solution tests 전체 156개 통과/실패 0.
