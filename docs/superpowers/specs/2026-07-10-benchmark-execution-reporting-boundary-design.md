# Benchmark 실행/분석 책임 경계 설계

- 상태: 초안 작성 및 자체 검토 완료, 사용자 문서 검토 대기
- 관련 결정: D013, D239

## 목표

`tests/Hps.Benchmarks`가 부하 실행과 artifact 분석을 함께 소유하는 현재 구조가 실제 유지보수 문제인지 판단하고,
읽는 사람이 책임을 구분할 수 있는 최소 경계를 정한다. 프로젝트 수와 workflow 연결을 늘리는 변경은 구체적인
효과가 확인될 때만 허용한다.

## 확인된 현재 상태

- `Hps.Benchmarks`에는 project file을 포함해 48개 파일이 있다.
- summary/history/envelope/report 계열은 32개 파일이고, 실제 runtime 또는 BenchmarkDotNet 의존 파일은 5개다.
- `Hps.Benchmarks.Tests` 16개 파일 중 12개가 reporting 또는 mixed CLI 계약을 검증한다.
- Windows/Linux benchmark workflow는 reporting command를 합계 9회 호출한다.
- 현재 사용자 문서 중 reporting command를 직접 안내하는 파일은 baseline index와 local baseline 문서 2개다.
- reporting generator/reader/writer를 `src/` 또는 `samples/`에서 직접 사용하는 소비자는 없다.
- 실행 경로는 `TcpLoopbackReportWriter`가 raw JSON을 만들고, reporting 경로는 `BaselineReportReader`가 그 파일을
  다시 읽는다. 즉 메모리 객체가 아니라 raw report 파일이 이미 자연스러운 책임 경계다.

## 대안 비교

### A. 단일 executable 유지 + 논리적 파일 경계 명시

현재 project와 command surface를 유지한다. 실행 영역은 raw report를 생산하고 reporting 영역은 파일만 소비한다.

- 장점: workflow와 사용법이 바뀌지 않고 새 project reference가 생기지 않는다.
- 장점: 현재 존재하는 파일 경계를 그대로 사용하므로 코드 이동이 없다.
- 단점: `Program`과 `BenchmarkCommandParser`는 두 command 계열을 계속 조합한다.
- 판단: 현재 요구와 비용에 가장 비례하므로 채택한다.

### B. 별도 `Hps.BenchmarkReports` executable로 물리 분리

summary/history/envelope command와 관련 파일을 새 console project로 옮긴다.

- 장점: reporting tool은 BenchmarkDotNet, Server, RIO, io_uring에 의존하지 않아도 된다.
- 장점: command와 assembly 책임이 가장 분명해진다.
- 단점: 약 32개 production 파일, 다수 test, parser, solution, workflow 9개 호출, 현재 사용 문서를 함께 이관해야 한다.
- 단점: 독립 배포나 외부 소비자가 없는 현재에는 구조 이동 비용이 기능 가치보다 크다.
- 판단: trigger가 생길 때만 재평가한다.

### C. reporting class library만 분리하고 CLI는 유지

reporting 구현을 library로 옮기고 기존 `Hps.Benchmarks`가 참조한다.

- 장점: compile-time dependency 방향은 분리된다.
- 단점: 사용자와 workflow가 보는 tool 경계는 그대로인데 project와 friend/test 연결만 늘어난다.
- 판단: 읽기 부담을 줄이지 못하면서 연결만 늘리므로 제외한다.

## 결정

현재는 project, namespace, executable을 분리하지 않는다. 대신 다음 논리적 경계를 D239로 고정한다.

### 실행 영역

- `--target`, `--smoke`, `--load`, `--load-open-loop`, `--baseline-suite`
- BenchmarkDotNet benchmark
- TCP/UDP loopback runner와 transport backend 선택
- `TcpLoopbackRunResult`, `TcpLoopbackReportWriter`, `BaselineSuiteRunner`
- 산출물: raw benchmark report JSON

### Reporting 영역

- `--summarize-baseline`
- `--summarize-baseline-history`
- `--compare-baseline-envelope`
- `BaselineReportReader` 이후 summary/history/envelope model, generator, JSON/Markdown writer
- 입력: raw report 또는 이전 reporting 단계가 생성한 JSON 파일

### 허용되는 연결

`Program`과 `BenchmarkCommandParser`는 하나의 executable을 조합하는 composition root로 두 영역을 안다.
`BaselineSuiteRunner`는 실행 영역에 남고 raw report 생성까지만 책임진다.

Reporting 영역은 다음을 참조하지 않는다.

- `Hps.Server`, `Hps.Transport`, `Hps.Transport.Rio`, `Hps.Transport.IoUring`
- `TcpLoopbackScenarioRunner`, `UdpLoopbackScenarioRunner`
- `TcpLoopbackRunResult`
- socket, endpoint 또는 backend selection 타입

## 물리 분리 trigger

다음 중 하나가 실제로 발생할 때만 별도 reporting executable 설계를 다시 연다.

1. benchmark 실행 없이 reporting command만 사용하는 독립 소비자 또는 배포 단위가 생긴다.
2. reporting에 execution project와 양립하기 어려운 platform 또는 package 의존성이 필요해진다.
3. runner 변경이 reporting test/build를 반복적으로 깨뜨리거나 그 반대 방향의 회귀가 누적된다.
4. workflow가 report tool만 별도로 restore/build/cache해야 할 측정 가능한 시간 또는 안정성 문제를 보인다.

파일 수가 많다는 사실이나 폴더를 보기 좋게 정리하려는 목적만으로는 물리 분리하지 않는다.

## 검증과 운영 규칙

- 새 reporting 기능은 raw/summary/history JSON 경계로 입력을 받으며 runner를 직접 호출하지 않는다.
- 새 실행 기능은 raw report schema까지 책임지고 summary/history/envelope 정책을 직접 계산하지 않는다.
- reporting 변경은 관련 generator/writer test와 workflow static contract test를 실행한다.
- execution 변경은 runner/baseline suite test와 4096 bytes x 100 Hz 기준을 검증한다.
- 두 영역의 JSON 계약을 바꾸는 경우에만 양쪽 test를 함께 변경한다.

## 이번 범위에서 하지 않는 일

- 새 project, solution entry 또는 project reference 추가
- namespace/folder 대량 이동
- parser 또는 command model 재작성
- workflow command 변경
- raw/summary/history/envelope schema 변경
- 새 benchmark report 기능 추가

## 완료 판단

이번 설계 단위는 코드 변경으로 이어지지 않는다. 현재 혼합 구조는 논리적 파일 경계가 이미 있고 외부 소비자가 없으므로,
물리 분리는 과엔지니어링으로 판단한다. D239 경계를 위반하는 실제 요구가 생길 때만 새 구현 설계를 작성한다.
