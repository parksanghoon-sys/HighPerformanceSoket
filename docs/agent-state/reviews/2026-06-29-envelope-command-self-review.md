# Envelope Command 구현 self-review

## Scope

- 검토 범위: runner/profile scoped envelope comparison command Task 1~4 구현, D125 설계 문서, 구현 계획, writer/Program smoke 경로.
- 핵심 목적: `--compare-baseline-envelope`가 reference `history.json`과 candidate `summary.json`/`history.json`을 읽어 별도 JSON/Markdown envelope artifact 를 만들고, signal 을 process failure 로 승격하지 않는지 확인한다.
- 범위 밖: warning-as-failure, CI hard gate, latency hard gate 승격, summary/history 기존 warning threshold 변경.

## Findings

### F1. Major / maintainability, contract

- Evidence: D125 schema 는 `reference-history-path`, `candidate-path`, `candidate-kind`, `reference-summary-count`, `candidate-summary-count`, `envelope-mismatches`, signal `code`를 명시한다. Task 4 최초 writer 는 `reference-source-path`, `candidate-source-path`, `mismatches`를 쓰고 source count 와 signal code 를 누락했다.
- Impact: envelope artifact 를 읽는 후속 자동화나 수동 리뷰 문서가 설계 schema 와 다른 필드를 보게 되어, 첫 소비자 구현 시 불필요한 호환 분기나 schema 재작업이 생긴다.
- Recommendation: writer schema 를 D125 이름으로 맞추고, source kind/count 와 signal code 를 테스트로 고정한다.
- Status: 반영 완료. `BaselineEnvelopeComparisonWriterTests`와 `BaselineEnvelopeProgramTests`가 schema field 를 직접 검증한다.

### F2. Minor / operability

- Evidence: 로컬 기본 `dotnet`은 `global.json`이 없어 SDK 10.0.203을 선택한다. 이 상태에서 `dotnet build tests\Hps.Benchmarks\Hps.Benchmarks.csproj --no-restore`는 BenchmarkDotNet transitive analyzer metadata `CS0006`로 실패했다. 같은 restore 산출물은 .NET 9.0.314 MSBuild로 통과한다.
- Impact: 코드 문제는 아니지만, 다음 작업자가 기본 `dotnet build`를 실행하면 재현성 없는 실패를 먼저 만나게 된다.
- Recommendation: net9.0 프로젝트 의도에 맞춰 SDK 선택을 고정하거나, 검증 문서에 9.0 SDK 명시를 남기는 인프라 단위를 다음 작업으로 처리한다.
- Status: 다음 Current TODO 로 승격한다.

## Material failure modes

- Trigger: envelope JSON 소비자가 D125 field name 을 기준으로 `envelope-mismatches` 또는 `candidate-kind`를 읽는다.
- Impact: 필드 누락으로 후속 비교 자동화가 실패하거나, 실제 mismatch/signal 이 없는 것처럼 오판할 수 있다.
- Detection: writer/Program tests 에서 D125 top-level field 와 signal code 를 직접 assert 한다.
- Mitigation: writer schema 를 D125로 정렬하고 CLI smoke 로 실제 artifact field 를 확인했다.

## Deferred items

- SDK 선택 재현성: `global.json` pin 또는 동등한 검증 환경 고정이 필요하다. 다음 작업 후보로 올렸다.
- Warning-as-failure/CI latency hard gate: D125 범위 밖이며 여전히 보류한다.

## Unresolved decisions that may bite you later

- Envelope artifact v1 field 를 후속 소비자가 실제로 읽기 시작하면, 지금 schema 를 기준으로 유지할지 또는 추가 compatibility reader 를 둘지 결정해야 한다. 현재는 첫 구현 직후라 legacy compatibility 를 추가하지 않는다.

## Completion summary

- Reviewed scope: envelope comparison parser/reader/generator/writer/Program wiring.
- Major findings: D125 JSON schema field drift 1건을 발견했고 즉시 수정했다.
- Key risks: 기본 SDK 10.0.203 선택 시 benchmark project build 실패가 남아 있다.
- Deferred items: SDK 선택 재현성 hardening.
- Unresolved important decisions: envelope artifact v1 소비자 도입 시 compatibility 정책.
