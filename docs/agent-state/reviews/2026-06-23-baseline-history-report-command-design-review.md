# Baseline history report command 설계 리뷰

- 날짜: 2026-06-23
- 대상: `docs/superpowers/specs/2026-06-23-baseline-history-report-command-design.md`
- 결과: 보정 후 승인

## 1. Scope

- 리뷰 범위: baseline history report command 설계가 D069/D070/D071, 현재 benchmark CLI 구조, baseline index 운영 방식과 충돌하지 않는지 확인했다.
- 핵심 목적: 여러 baseline session `summary.json`을 읽어 `history.json`과 선택적 `history.md`를 만드는 provider-independent command 범위를 구현 전에 고정한다.
- 범위 밖: CI workflow, warning-as-failure, latency hard gate, runner identity/environment metadata, 기존 `index.md` 자동 덮어쓰기.

## 2. Findings

### Finding 1

- **Severity**: Major
- **Dimension**: maintainability
- **Evidence**: Task 1이 `BenchmarkCommand.HistoryBaseline` 또는 `SummarizeBaselineHistory` 중 하나를 추가한다고 적고 있었다.
- **Impact**: 다음 구현자가 enum 이름을 임의 선택하면 parser test, usage text, Program switch 이름이 설계와 달라질 수 있다. command 이름은 이미 `--summarize-baseline-history`로 정해졌으므로 enum 도 같은 의미를 유지해야 한다.
- **Recommendation**: `BenchmarkCommand.SummarizeBaselineHistory`로 고정한다.
- **Resolution**: 설계 문서에 enum 이름을 `SummarizeBaselineHistory`로 고정했다.

### Finding 2

- **Severity**: Major
- **Dimension**: correctness
- **Evidence**: 설계는 입력 root 로 `docs/benchmarks/baselines`와 특정 날짜 directory 를 모두 허용하지만, discovery 예시는 `<YYYY-MM-DD>/summary.json`과 `<YYYY-MM-DD>/session-NN/summary.json`만 적고 있었다.
- **Impact**: 입력 root 가 이미 `2026-06-18` 같은 날짜 directory 인 경우 구현자가 `<root>/<YYYY-MM-DD>/...` 형태만 찾거나, 반대로 parent root 와 date root 를 같은 방식으로 처리해 root summary 를 놓칠 수 있다.
- **Recommendation**: parent baseline root 와 date root 입력을 분리하고, 둘 다 bounded discovery 로 명시한다.
- **Resolution**: 설계 문서에 date root 입력과 parent root 입력의 탐색 규칙을 분리해 적었다.

## 3. Material failure modes

- **Trigger**: date root 를 직접 입력했는데 reader 가 parent root 방식으로만 탐색한다.
- **Impact**: 이미 존재하는 `2026-06-18/summary.json`을 못 찾아 usage error 또는 빈 history 가 생긴다.
- **Detection**: `docs/benchmarks/baselines/2026-06-18` smoke 에서 `session-01(root)` entry 가 누락된다.
- **Mitigation**: 구현 계획과 reader tests 에 parent root 입력과 date root 입력을 모두 포함한다.

## 4. Deferred items

- runner identity/environment metadata 는 이번 command schema 에 추가하지 않는다. 서로 다른 runner latency 비교 자동화가 필요해질 때 별도 설계한다.
- 기존 `docs/benchmarks/baselines/index.md` 자동 덮어쓰기는 보류한다. history command 는 별도 generated Markdown 을 먼저 만든다.

## 5. Unresolved decisions that may bite you later

- warning-as-failure 승격 조건은 아직 열려 있다. 현재 command 는 warning 을 soft signal 로 aggregate 만 해야 한다.
- 날짜가 다른 baseline 을 latency regression 으로 비교하려면 runner identity 와 환경 metadata 가 먼저 필요하다.

## 6. Completion summary

- Reviewed scope: baseline history report command 설계, D069/D070/D071 정책, benchmark CLI/parser 구조, baseline index 운영 방식.
- Major findings: enum 이름 모호성, parent root/date root discovery 모호성을 발견했고 설계 문서에서 바로 해소했다.
- Key risks: 구현 시 date root smoke 를 빠뜨리면 `session-01(root)` 호환 경로가 깨질 수 있다.
- Deferred items: CI workflow, warning-as-failure, latency hard gate, runner metadata, `index.md` 자동 갱신.
- Unresolved important decisions: warning 승격과 runner identity 기반 비교 정책.
