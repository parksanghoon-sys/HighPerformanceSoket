# Benchmark runner identity / baseline comparison readiness 설계

- 날짜: 2026-06-23
- 상태: Accepted
- 관련 결정: D063, D069, D070, D071, D078, D079
- 관련 코드: `tests/Hps.Benchmarks`, `tests/Hps.Benchmarks.Tests`
- 관련 artifact:
  - `docs/benchmarks/baselines/index.md`
  - `docs/benchmarks/baselines/**/summary.json`
  - `docs/benchmarks/baselines/**/history.json`

## 목적

baseline history command 로 여러 session 을 한 번에 읽는 기반은 생겼지만, 아직 각 run 이 어떤 runner 와 환경에서 측정됐는지
machine-readable 하게 남기지 않는다. 이 상태에서 날짜가 다른 session 이나 CI/self-hosted/local runner 값을 같은 latency envelope 로
직접 비교하면 false signal 이 생길 수 있다.

이번 설계의 목적은 warning-as-failure, latency hard gate, CI workflow 보다 먼저 필요한 **runner identity / environment metadata**를
작고 후방 호환적인 방식으로 고정하는 것이다.

## 현재 확인한 사실

- raw report schema v1 은 측정값과 hard pass/fail 만 기록한다.
- `summary.json`과 `history.json`은 raw report 에 없는 runner 정보를 새로 만들 수 없다.
- `docs/benchmarks/baselines/index.md`에는 `local Windows TCP loopback SAEA` 같은 사람이 적은 runner/scope 문자열이 있지만,
  JSON artifact 에 같은 정보가 없다.
- D071/D078은 runner identity/environment metadata 부재 때문에 warning-as-failure 와 latency regression 판정을 후속으로 남겼다.
- 현재 benchmark runner 는 SAEA TCP loopback 기준선에 집중한다. transport backend 는 실제 코드상 `SaeaTransport` 경로다.

## 문제 정의

runner identity 없이 history 를 비교하면 아래 문제가 생긴다.

- 같은 날짜의 같은 장비 반복값과 다른 장비의 값이 같은 비교군으로 섞인다.
- OS/runtime/architecture 차이로 생긴 latency 변화가 코드 regression 처럼 보일 수 있다.
- CI 에서 warning 을 실패로 승격할 때, runner 교체나 hosted runner 변동이 false failure 를 만들 수 있다.
- 기존 index 의 runner/scope 텍스트는 사람이 읽기에는 충분하지만, 자동화가 안전하게 비교군을 나누기에는 부족하다.

## 결정

다음 구현 후보는 **raw benchmark report 에 runner/environment metadata 를 additive field 로 기록하고, summary/history 가 이를
보존·집계할 수 있게 만드는 것**으로 둔다.

schema 는 기존 raw report `schema-version: 1`을 유지한다. D055처럼 optional additive field 로 추가해 기존 artifact reader 와
summary/history command 가 legacy report 를 계속 읽을 수 있게 한다.

## Raw report metadata 초안

새 필드는 raw report top-level 에 둔다. 측정값과 pass/fail 판정은 기존 field 를 그대로 유지한다.

- `benchmark-profile`: 현재 runner family 를 나타내는 stable 값. 초기값은 `tcp-loopback-saea-v1`.
- `runner-id`: 같은 runner 로 볼 수 있는 명시 식별자. `HPS_BENCHMARK_RUNNER_ID` 환경 변수가 있으면 그 값을 쓰고,
  없으면 `local-unspecified`를 쓴다.
- `runner-kind`: runner 종류. `HPS_BENCHMARK_RUNNER_KIND` 환경 변수가 있으면 그 값을 쓰고, 없으면 `local`을 쓴다.
- `transport-backend`: 현재 실제 backend. 초기값은 `SaeaTransport`.
- `os-description`: `RuntimeInformation.OSDescription`.
- `os-architecture`: `RuntimeInformation.OSArchitecture`.
- `process-architecture`: `RuntimeInformation.ProcessArchitecture`.
- `framework-description`: `RuntimeInformation.FrameworkDescription`.
- `processor-count`: `Environment.ProcessorCount`.

기본값은 privacy 를 우선한다. host name, user name, full path, IP address 는 자동으로 기록하지 않는다. 여러 장비를 명확히
구분해야 하는 사용자는 `HPS_BENCHMARK_RUNNER_ID`를 명시한다.

## Summary/history 집계 규칙

summary 와 history 는 raw report 의 metadata 를 그대로 읽어 비교 가능성 판단 신호를 제공한다.

- legacy raw report 에 metadata 가 없으면 `unknown` 계열 값으로 보존한다.
- summary 는 포함된 raw report 들의 metadata 가 모두 같은지 판단해 `comparison-compatible` 같은 boolean 신호와
  mismatch reason 을 만들 수 있다.
- history 는 session summary 들의 runner identity 가 모두 같은지 판단할 수 있어야 한다.
- mismatch 는 초기에는 failure 가 아니다. warning-as-failure 전 단계의 review signal 로만 둔다.

초기 구현에서 summary/history output 에 넣을 최소 field 는 다음 구현 계획에서 확정한다. 단, raw report 가 원천 정보를
잃지 않도록 기록하는 것이 우선이다.

## 비교 가능성 기준

자동 latency 비교를 허용하려면 최소한 아래 값이 같아야 한다.

- `benchmark-profile`
- `runner-id`
- `runner-kind`
- `transport-backend`
- `os-description`
- `os-architecture`
- `process-architecture`
- `framework-description`
- payload/target 설정: `scenario`, `payload-bytes`, `target-rate-hz`, `target-duration-seconds`

`processor-count`는 비교 보조 신호로 기록하되, 초기 hard compatibility key 에 바로 넣지는 않는다. VM/CI 에서는 논리 CPU 수가
runner 재시작에 따라 바뀔 수 있고, 이 값만으로 false mismatch 를 만들 수 있기 때문이다.

## 범위 밖

- warning-as-failure 옵션 또는 CI failure 정책 구현
- latency hard threshold 확정
- Git commit hash 자동 수집
- host name, user name, IP address 자동 기록
- 기존 baseline artifact 대량 재작성
- generated `index.md` 자동 갱신
- RIO/io_uring benchmark profile 추가

## 다음 구현 후보

사용자 검토 후 구현 계획은 다음 순서가 가장 작다.

1. raw report identity capture/write/read
   - `BenchmarkRunIdentity` 같은 내부 model 을 만들고 `TcpLoopbackReportWriter`와 `BaselineReportReader`에 연결한다.
   - legacy raw report 는 metadata 없음 상태로 계속 읽는다.
2. summary/history comparison signal
   - summary/history 가 runner metadata 일치 여부를 JSON/Markdown 에 표시한다.
   - mismatch 는 exit code 에 영향을 주지 않는다.
3. warning-as-failure 또는 CI workflow 재검토
   - 같은 runner identity 의 반복 baseline 이 충분해진 뒤에만 실패 승격을 다시 판단한다.

각 구현 후보는 별도 커밋 단위로 나눈다. 이번 설계 단위에서는 코드를 바꾸지 않는다.

## 검증 계획

이 설계 단위의 검증은 문서 정합성 중심이다.

- D069/D070/D071/D078과 충돌하지 않는지 확인한다.
- raw report writer/reader/source model 이 실제로 metadata 추가 지점을 가질 수 있는지 source 를 대조한다.
- privacy-sensitive 자동 수집 항목이 들어가지 않았는지 확인한다.
- `git diff --check`로 whitespace 오류를 확인한다.
- 문서 변경이지만 repository 현재 상태 확인을 위해 solution build/test 를 수행한다.

## Self-review

- placeholder 없음.
- schema version 은 기존 additive field 정책과 맞춘다.
- runner identity 를 자동 host name 으로 채우지 않아 로컬/사설 환경 정보 노출을 피한다.
- warning-as-failure 와 latency hard gate 는 여전히 보류한다.
- 다음 구현은 raw report metadata 부터 시작하므로 summary/history 비교 로직보다 원천 데이터 보존을 우선한다.
