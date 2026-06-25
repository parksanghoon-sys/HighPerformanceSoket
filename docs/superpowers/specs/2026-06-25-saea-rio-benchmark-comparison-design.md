# SAEA vs RIO benchmark comparison 설계

## 배경

Phase 5 Windows RIO backend 는 TCP listen/connect/accept/receive/send opt-in 경로를 갖췄고,
default factory 는 D097/D098/D100 기준으로 계속 SAEA를 유지한다.
PLAN Phase 5 완료 기준에는 RIO backend 통합 테스트 green 과 함께 Phase 4 benchmark 로 SAEA 대비 개선 확인이 포함된다.

현재 `tests/Hps.Benchmarks`의 TCP loopback runner 는 `TcpLoopbackScenarioRunner` 내부에서 `SaeaTransport`를 직접 생성한다.
raw report schema v1은 이미 `benchmark-profile`, `transport-backend`, `scenario`, runner identity 를 기록한다.
따라서 public `ITransport`/`TransportFactory` 계약을 넓히지 않고 benchmark 내부 선택지만 추가하면 SAEA/RIO 비교를 만들 수 있다.

## 목표

- 같은 TCP loopback benchmark harness 로 SAEA와 RIO를 명시적으로 실행한다.
- raw report 와 summary/history 비교에서 SAEA/RIO 결과가 같은 baseline 으로 섞이지 않게 한다.
- RIO가 unavailable 인 환경에서는 명시적인 실패/skip 정책을 둔다.
- latency hard gate 는 계속 승격하지 않고 report-only 비교 artifact 로 둔다.

## 비목표

- `TransportFactory.CreateDefault()`를 RIO로 바꾸지 않는다.
- public `ITransport` 계약에 backend selector 를 추가하지 않는다.
- RIO UDP benchmark, batching, IOCP notification 기반 RIO benchmark 는 이번 범위가 아니다.
- repository baseline 자동 채택이나 CI latency gate 를 추가하지 않는다.

## 설계 결정

### 1. benchmark 내부 backend selector 만 추가한다

`tests/Hps.Benchmarks` 내부에 `TcpLoopbackTransportBackend` 같은 internal enum 을 둔다.

- `Saea`: 기존 경로. 기본값.
- `Rio`: `Hps.Transport.Rio.RioTransport`를 명시 생성한다.

`TcpLoopbackScenarioRunner`는 `RunSmokeAsync()`, `RunLoadAsync()`, `RunOpenLoopAsync()` overload 또는 options 객체로 backend 를 받는다.
기존 호출자는 SAEA 기본값을 그대로 사용하므로 기존 CLI와 baseline suite 는 깨지지 않는다.

### 2. CLI는 실행 명령에만 `--backend <saea|rio>`를 허용한다

허용 명령:

- `--smoke [--backend <saea|rio>] [--report <path>]`
- `--load [--backend <saea|rio>] [--report <path>]`
- `--load-open-loop [--backend <saea|rio>] [--report <path>]`
- `--baseline-suite <output-dir> [--runs <count>] [--backend <saea|rio>]`

summary/history 명령은 이미 raw report 의 `transport-backend`와 comparison key 를 읽으므로 backend 옵션을 받지 않는다.
`--backend` 단독 또는 summary/history 와 조합하면 usage error 로 처리한다.

### 3. RIO unavailable 은 explicit run failure 로 처리한다

사용자가 `--backend rio`를 명시했는데 `RioCapabilityProbe.GetStatus()`가 `Available`이 아니면 run 은 실패한다.
이 경우 usage error 가 아니라 execution failure 로 둔다.

이유:

- 명령 자체는 올바르다.
- 해당 runner 의 OS/socket provider capability 가 실행 조건을 만족하지 못한 것이다.
- CI나 수동 benchmark 에서 RIO 미가용을 조용히 SAEA fallback 으로 바꾸면 비교 artifact 가 오염된다.

### 4. report identity 는 backend 별로 달라야 한다

SAEA:

- `benchmark-profile`: `tcp-loopback-saea-v1`
- `transport-backend`: `SaeaTransport`
- `scenario`: 기존 `tcp-loopback-saea-baseline`, `tcp-loopback-saea-baseline-open-loop`, smoke suffix 유지

RIO:

- `benchmark-profile`: `tcp-loopback-rio-v1`
- `transport-backend`: `RioTransport`
- `scenario`: `tcp-loopback-rio-baseline`, `tcp-loopback-rio-baseline-open-loop`, smoke 는 `tcp-loopback-rio-baseline-smoke`

기존 summary/history comparison key 는 runner id, kind, transport backend, benchmark profile, OS/runtime, case list 를 포함한다.
따라서 SAEA/RIO raw report 를 같은 directory 에 섞으면 comparison mismatch/warning 이 발생해야 한다.
정상 운영은 backend 별 output directory 를 분리한다.

권장 저장 위치:

- SAEA reference: 기존 `docs/benchmarks/baselines/runners/<runner-id>/<date>/session-NN/`
- RIO reference: `docs/benchmarks/baselines/runners/<runner-id>/<date>/rio-session-NN/` 또는 별도 runner root

다만 현재 `BaselineHistoryReader`는 `session-NN` convention 을 기준으로 읽는다.
따라서 이번 구현의 최소 범위에서는 RIO repository baseline 채택 구조를 새로 만들지 않고,
raw report artifact 를 별도 scratch/output directory 로 생성한 뒤 사람이 비교한다.
repository baseline 채택은 후속 설계로 둔다.

### 5. schema-version 은 유지한다

raw report schema v1은 이미 backend/profile/scenario 를 additive field 로 갖고 있다.
새 key 를 추가하지 않고 기존 identity 값을 backend 별로 다르게 채운다.
summary/history writer 도 schema 변경 없이 comparison mismatch 를 감지할 수 있어야 한다.

## 구현 계획

1. `TcpLoopbackTransportBackend`와 backend options/value object 를 추가한다.
2. `TcpLoopbackScenarioRunner`가 backend 에 따라 `SaeaTransport` 또는 `RioTransport`를 생성하도록 바꾼다.
   RIO 참조는 benchmark project 에 `Hps.Transport.Rio` project reference 를 추가한다.
3. `BenchmarkRunIdentity`에 backend/profile override factory 를 추가한다.
   기본 `CaptureDefault()`는 그대로 SAEA를 반환한다.
4. `BenchmarkCommandParser`가 runner/baseline-suite 명령에서 `--backend` 옵션을 파싱한다.
5. `Program`이 선택된 backend 를 runner 에 전달한다.
6. 테스트:
   - parser: `--load --backend rio --report x` 파싱.
   - parser: summary/history 에 `--backend` 사용 시 usage error.
   - identity/report: RIO result 가 `transport-backend=RioTransport`, `benchmark-profile=tcp-loopback-rio-v1`,
     RIO scenario 를 기록.
   - runner: RIO unavailable 환경에서는 explicit RIO run 이 fail 로 끝남.
   - smoke/live: RIO available Windows 에서 `--smoke --backend rio --report <temp>`가 pass.

## 검증 계획

- `dotnet test tests\Hps.Benchmarks.Tests\Hps.Benchmarks.Tests.csproj --no-restore`
- `dotnet test tests\Hps.Transport.Rio.Tests\Hps.Transport.Rio.Tests.csproj --no-restore`
- `dotnet build HighPerformanceSocket.slnx --no-restore`
- `dotnet test HighPerformanceSocket.slnx --no-build --no-restore`
- `git diff --check`

## 미결/후속

- RIO raw report 를 repository baseline 으로 채택할 canonical directory convention 은 별도 결정이 필요하다.
- SAEA/RIO 비교 summary 를 하나의 Markdown 으로 묶는 report command 는 후속이다.
- RIO latency hard gate, CI warning-as-failure, automatic default backend selection 은 계속 보류한다.
