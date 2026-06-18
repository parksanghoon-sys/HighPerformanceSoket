# CI/반복 Baseline 확대 정책 설계

- 날짜: 2026-06-18
- 상태: Accepted
- 범위: Phase 4 benchmark report 를 기반으로 hard latency gate 이전에 어떤 baseline 을 먼저 쌓을지 결정한다.

## 목표

4096B x 100Hz Interface Server 목표를 delivery/drop/leak 관점뿐 아니라 latency 추세 관점에서도 반복 검증할 수 있게 만든다.
다만 현재 단계에서는 p50/p99 절대값을 CI hard failure 로 승격하지 않고, 재현 가능한 baseline artifact 를 먼저 축적한다.

## 현재 확인된 사실

- `tests/Hps.Benchmarks`는 `--smoke`, `--load`, `--load-open-loop` runner 를 제공한다.
- 세 runner 는 `TcpLoopbackRunResult`와 `TcpLoopbackReportWriter`를 통해 같은 JSON schema v1을 쓴다.
- report writer 는 상위 directory 를 만들고 기존 파일은 덮어쓴다.
- `TcpLoopbackRunResult.Passed`는 planned/sent/received 일치, dropped 0, payload-errors 0, pool-rented 0만 hard gate 로 본다.
- 2026-06-18 로컬 baseline 에서 `--load` 3회와 `--load-open-loop` 3회는 모두 pass 했다.
- 같은 baseline 의 closed-loop p99는 879.7~924.1us, open-loop p99는 915.9~1005.5us 였고 TCP high-watermark 는 각각 1/2였다.

## 검토한 선택지

### 선택지 A: 수동 로컬 baseline 만 유지

가장 작은 변화지만, 날짜와 장비가 다른 반복 결과를 구조적으로 모으지 못한다.
다음 작업자가 threshold 를 정하려면 다시 수동 절차와 파일 위치를 재발견해야 한다.

### 선택지 B: 지금 p99 hard threshold 를 추가

자동화 관점에서는 명확하지만 false negative 위험이 크다.
현재 수치는 단일 개발 PC의 같은 날 실행값이며 OS scheduling, 백그라운드 부하, JIT/워밍업 상태 영향을 분리하지 못한다.
delivery/drop/leak 는 이미 hard gate 로 충분히 방어되고 있으므로, latency 숫자까지 즉시 실패 조건으로 올릴 근거는 부족하다.

### 선택지 C: 반복 baseline artifact 를 먼저 축적

권장 방향이다.
기존 JSON schema v1을 유지하고, `--load`와 `--load-open-loop` 결과를 원본 JSON artifact 로 남긴다.
hard pass/fail 은 현행 delivery/drop/leak 조건으로 유지하되, p50/p99, p99 growth ratio, actual-rate, TCP/UDP high-watermark 는 trend 분석용으로 축적한다.

## 결정

v1에서는 latency hard gate 를 추가하지 않는다.
다음 구현 단위는 threshold 추가가 아니라 반복 baseline 수집 절차를 자동화하거나 문서화하는 방향으로 잡는다.

반복 baseline 은 최소 다음 기준을 만족할 때 hard gate 재검토 후보가 된다.

- 같은 장비 또는 같은 CI runner 에서 서로 다른 시점의 baseline session 을 3개 이상 확보한다.
- 각 session 은 `--load` 3회와 `--load-open-loop` 3회를 포함한다.
- 모든 run 은 `sent == planned-message-count`, `sent == received`, `dropped == 0`, `payload-errors == 0`, `pool-rented == 0`을 만족해야 한다.
- raw JSON report 를 보존하고, summary 는 JSON에서 재생성 가능해야 한다.
- hard gate 이전에는 soft warning 후보만 산출한다.

## JSON schema 정책

- `schema-version`은 report key 집합이 바뀔 때만 올린다.
- smoke/load/open-loop 는 같은 key 집합을 항상 출력한다.
- latency trend 값이 smoke 에서 약한 의미만 갖더라도 key 는 누락하지 않는다.
- 반복 baseline summary 는 기존 per-run JSON을 입력으로 만들고, per-run JSON을 대체하지 않는다.

## 다음 구현 후보

다음 단위는 작게 유지한다.

1. `tests/Hps.Benchmarks`에 반복 baseline collection command 를 추가할지 검토한다.
2. 추가한다면 `--load`와 `--load-open-loop`를 지정 횟수 실행하고 per-run JSON과 summary 를 남기되, latency hard failure 는 만들지 않는다.
3. CI script 가 생긴다면 raw JSON artifact 를 먼저 보존하고, summary 는 리뷰 편의 출력으로만 둔다.

## 제외 범위

- p50/p99 절대 threshold 확정
- baseline 대비 상대 regression 비율 확정
- hard failure / soft warning 경계 확정
- Markdown report generator 구현
- CI provider 별 workflow 구현
- RIO/io_uring backend 성능 목표 확정

## 검증 계획

이번 단위는 정책/설계 문서만 변경한다.
문서 일관성과 state doc 연결을 확인하고 `git diff --check`로 whitespace 오류를 검증한다.
