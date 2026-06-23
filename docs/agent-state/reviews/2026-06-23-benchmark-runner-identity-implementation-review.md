# Benchmark runner identity 구현 검토

- 날짜: 2026-06-23
- 대상 커밋: `fbc30fb`, `59f8bb8`, `685483b`
- 관련 결정/설계: D079, `docs/superpowers/specs/2026-06-23-benchmark-runner-identity-design.md`
- 구현 계획: `docs/superpowers/plans/2026-06-23-benchmark-runner-identity.md`

## 1. Scope

- 리뷰 범위: `BenchmarkRunIdentity` model, raw report writer metadata, raw report reader/legacy fallback, 관련 focused tests, root 상태 문서.
- 핵심 목적: raw report schema v1을 유지하면서 runner/environment metadata를 additive field로 기록하고, metadata 없는 legacy report도 계속 읽게 한다.
- 범위 밖: summary/history comparison signal, warning-as-failure, latency hard gate, CI workflow, generated index 자동 갱신.

## 2. Findings

### Finding 1

- **Severity**: Minor
- **Dimension**: testing
- **Evidence**
  - D079 raw metadata field에는 `os-architecture`, `process-architecture`가 포함된다.
  - 실제 writer는 두 field를 쓴다: `tests/Hps.Benchmarks/TcpLoopbackReportWriter.cs`.
  - reader는 두 field를 읽는다: `tests/Hps.Benchmarks/BaselineReportReader.cs`.
  - 하지만 `Write_WhenRunResultIsWritten_IncludesRunnerIdentityMetadata`는 실제 writer output에서 두 field 존재를 직접 assert하지 않는다.
- **Impact**
  - 현재 구현은 코드 대조상 정합하지만, 이후 writer field 이름이 틀어져도 writer shape test만으로는 architecture field 누락을 바로 잡지 못할 수 있다.
  - reader test는 handcrafted JSON을 사용하므로 writer와 reader 사이의 완전 roundtrip 계약을 고정하지 않는다.
- **Recommendation**
  - 다음 test-hardening 단위나 summary/history comparison signal 구현 전에, 실제 `TcpLoopbackReportWriter` output을 `BaselineReportReader`로 다시 읽는 roundtrip test를 추가하거나 writer shape test가 D079 field 전체를 assert하게 보강한다.

## 3. Material Failure Modes

### Future writer field drift

- **Trigger**: 이후 writer 수정에서 `os-architecture` 또는 `process-architecture` field 이름이 reader와 다르게 바뀜.
- **Impact**: comparison signal 단계에서 architecture mismatch를 정확히 판단하지 못하고 `unknown` 또는 누락 상태로 취급할 수 있다.
- **Detection**: writer output 전체 field assertion 또는 writer-to-reader roundtrip test.
- **Mitigation**: 위 Finding 1의 test hardening을 별도 작은 단위로 처리한다.

## 4. Deferred Items

- `P3_NICE`: writer shape test를 D079 전체 field assertion 또는 writer-to-reader roundtrip test로 보강한다.
- `P1_SOON`: summary/history comparison signal 설계를 진행한다. raw report metadata 원천 기록과 reader 보존은 준비됐다.

## 5. Unresolved Decisions That May Bite You Later

- summary/history output에 어떤 field 이름으로 comparison compatibility를 표현할지 아직 미정이다.
  - 후보: `comparison-compatible`, mismatch reason list, by-run/by-session metadata summary.
  - 현재 단계에서는 raw metadata 원천 보존만 완료했다.
- mismatch가 warning-only인지, 언제 warning-as-failure로 승격되는지 아직 D079 후속 범위로 남아 있다.

## 6. Completion Summary

- Reviewed scope: benchmark runner identity Task 1~3 구현과 관련 tests/state docs.
- Major findings: 없음.
- Key risks: 현재 코드 결함은 보이지 않지만, writer/reader field drift를 더 강하게 잡는 roundtrip test는 아직 없다.
- Deferred items: writer metadata roundtrip test hardening, summary/history comparison signal 설계.
- Unresolved important decisions: comparison signal schema와 warning-as-failure 승격 정책.
