# Baseline history report command 구현 검토

- 날짜: 2026-06-23
- 대상 커밋 범위: `c667b99` ~ `a6a0dec`
- 검토 범위:
  - `tests/Hps.Benchmarks/BenchmarkCommand*.cs`
  - `tests/Hps.Benchmarks/BaselineHistory*.cs`
  - `tests/Hps.Benchmarks/Program.cs`
  - `tests/Hps.Benchmarks.Tests/*BaselineHistory*Tests.cs`
  - D078 설계/계획 문서와 실제 `docs/benchmarks/baselines` smoke artifact
- 결과: 새 Blocker/Major finding 없음

## 1. Scope

- 검토 대상은 baseline history report command Task 1~4 전체 구현이다.
- 핵심 목적은 여러 baseline session `summary.json`을 읽어 provider-independent `history.json`과 선택 `history.md`를 만드는 CLI가 D078 계약과 일치하는지 확인하는 것이다.
- CI workflow, warning-as-failure, latency hard gate, runner identity metadata, 기존 `index.md` 자동 갱신은 이번 검토 범위 밖이다.

## 2. Findings

의미 있는 Blocker/Major correctness finding은 없다.

### Finding 1

- **Severity**: Minor
- **Dimension**: testing / usability
- **Evidence**: `--summarize-baseline-history ... --history out/history.json --history-md`처럼 `--history-md` 값이 빠진 경우 parser test는 오류 존재만 확인한다. 구현은 길이 검사를 먼저 수행하므로 `MessageHistoryOutputRequired` 계열로 수렴한다.
  관련 위치: `tests/Hps.Benchmarks/BenchmarkCommandParser.cs`의 `ParseSummarizeBaselineHistory(...)`, `tests/Hps.Benchmarks.Tests/BenchmarkCommandParserTests.cs`의 `TryParse_WhenSummarizeBaselineHistoryMarkdownMissingPath_ReturnsUsageError`.
- **Impact**: command 자체는 usage error로 막히므로 correctness 영향은 없다. 다만 사용자에게 더 정확한 메시지를 주려면 `--history-md` 값 누락과 `--history` 누락을 구분하는 테스트가 필요하다.
- **Recommendation**: 지금은 비차단으로 둔다. CLI 오류 메시지 품질을 정리하는 별도 단위가 생기면 summary/history parser의 optional Markdown path 누락 메시지를 같이 정리한다.

### Finding 2

- **Severity**: Minor
- **Dimension**: testing
- **Evidence**: reader 단위 테스트는 date root 직접 입력과 parent root 입력을 모두 검증한다. Program-level smoke 테스트는 parent root 입력만 검증한다.
  관련 위치: `BaselineHistoryReaderTests.ReadSessions_WhenInputIsDateRoot_ReadsRootSummaryAndChildSessions`, `BaselineHistoryProgramTests.Main_WhenHistoryCommandHasPassingSummaries_WritesJsonAndMarkdownAndReturnsSuccess`.
- **Impact**: `Program`은 `BaselineHistoryReader.ReadSessions(...)`를 직접 호출하므로 현재 구조에서는 중복 통합 테스트가 없어도 실제 위험은 낮다. 다만 date root 직접 CLI 입력을 사람이 많이 쓰게 되면 Program-level smoke coverage를 추가할 수 있다.
- **Recommendation**: 현재는 유지한다. CLI 사용 패턴이 date root 직접 입력 중심으로 바뀌면 Program test 1개를 추가한다.

## 3. Material failure modes

### Parent/date root discovery 오류

- **Trigger**: `docs/benchmarks/baselines` parent root 또는 `docs/benchmarks/baselines/2026-06-18` date root를 입력한다.
- **Impact**: root summary 또는 `session-NN/summary.json`을 놓치면 history session 수가 틀어지고 baseline trend artifact가 불완전해진다.
- **Detection**: reader tests가 parent/date root discovery를 분리해 검증하고, 실제 CLI smoke가 parent root 기준 `session-count: 3`을 확인했다.
- **Mitigation**: bounded discovery를 유지한다. recursive scan은 generated history나 임시 복사본을 중복 집계할 수 있으므로 도입하지 않는다.

### Hard gate 의미 왜곡

- **Trigger**: raw failure count가 0이지만 session `hard-passed`가 false인 summary가 들어온다.
- **Impact**: history가 PASS로 잘못 집계되면 delivery/drop/leak hard gate 실패를 놓칠 수 있다.
- **Detection**: `Generate_WhenSessionHardPassedIsFalseWithZeroRawFailures_MarksHistoryFailed`와 Program failed-summary 테스트가 session flag AND 계약을 검증한다.
- **Mitigation**: history root는 `hard-passed` AND와 `failed-session-count`만 사용한다. raw failure total은 현재 output root에 올리지 않는다.

### Missing p99 artifact 결함 은폐

- **Trigger**: `by-kind.load` 또는 `by-kind.open-loop`이 없는 partial summary를 읽는다.
- **Impact**: 누락 p99를 `0`으로 쓰면 매우 빠른 정상 값처럼 보일 수 있다.
- **Detection**: reader/writer tests가 nullable p99와 JSON `null`, Markdown `-`를 검증한다.
- **Mitigation**: p99 누락은 계속 `null`/`-`로 드러낸다.

## 4. Deferred items

- CLI 오류 메시지 정밀화: `--summary-md`/`--history-md` 값 누락과 필수 JSON output 누락을 더 정확히 구분한다. 현재는 usage error로 차단되므로 비차단이다.
- Program-level date-root smoke 추가: date root 직접 입력이 주요 workflow가 되면 통합 테스트 1개를 추가한다.
- CI workflow, warning-as-failure, latency hard gate, runner identity/environment metadata는 여전히 별도 설계 대상이다.
- 기존 `docs/benchmarks/baselines/index.md` 자동 갱신은 아직 보류한다. history command의 Markdown output이 충분히 안정화된 뒤 판단한다.

## 5. Unresolved decisions that may bite you later

- warning-as-failure 승격 조건은 아직 열려 있다. 현재 command는 D078대로 warning을 soft signal로만 집계한다.
- runner identity/environment metadata 없이 날짜가 다른 session을 latency regression으로 직접 비교하면 false signal이 생길 수 있다.
- history artifact를 CI 산출물로 보관할지, repo에 커밋할지, generated index로만 쓸지는 아직 결정하지 않았다.

## 6. Completion summary

- Reviewed scope: baseline history command parser, reader, aggregate writer, Program wiring, tests, D078 설계 정합성.
- Major findings: 없음.
- Key risks: discovery 중복/누락, hard gate 의미 왜곡, p99 누락 은폐는 현재 테스트와 구현으로 방어되어 있다.
- Deferred items: CLI 오류 메시지 품질, Program-level date-root smoke, CI/warning/hard-gate 후속 설계.
- Unresolved important decisions: warning 승격, runner identity, history artifact 운영 방식.

## 검증

- 실제 CLI smoke:
  - `dotnet run --no-build --no-restore --project tests\Hps.Benchmarks\Hps.Benchmarks.csproj -- --summarize-baseline-history docs\benchmarks\baselines --history <temp-json> --history-md <temp-md>`
  - 출력: `session-count: 3`, `hard-passed: true`, `warning-count: 0`
- 생성 JSON 확인:
  - `history-version: 1`
  - `session-count: 3`
  - `failed-session-count: 0`
  - `summary-path`는 `/` separator relative path
- 생성 Markdown 확인:
  - UTF-8 기준 한글 header 정상 표시
  - p99/HWM/session table 정상 표시
