# RIO UDP benchmark artifact 설계

## 배경

D110 이후 RIO UDP native operation, endpoint owner, receive loop, send loop, diagnostics parity 는 구현됐다.
다만 `TransportFactory.CreateDefault()`는 아직 `SaeaTransport`를 유지한다. 기본 backend 승격 판단에는 기능 parity 뿐 아니라
실제 UDP delivery/drop/leak/latency/HWM artifact 가 필요하다.

기존 Phase 4 benchmark 하니스는 TCP loopback 기준으로 `--backend <saea|rio>` selector, raw report JSON,
summary/history aggregate 를 이미 제공한다. raw report 는 `result-name`, `scenario`, `benchmark-profile`,
`transport-backend`, sent/received/drop/HWM/latency/pool field 를 갖고 있어 UDP에도 같은 schema 를 재사용할 수 있다.

## 목표

- 같은 benchmark CLI 에서 TCP와 UDP loopback scenario 를 선택 실행할 수 있게 한다.
- UDP closed-loop/load 와 open-loop artifact 를 SAEA/RIO backend 별로 수집할 수 있게 한다.
- raw report schema 는 새 field 없이 재사용하고, `benchmark-profile`/`scenario` 값으로 TCP/UDP를 구분한다.
- repository baseline 채택 전에 scratch artifact 로 RIO UDP readiness evidence 를 수집한다.
- RIO no-prefetch(D111) 때문에 open-loop에서 loss가 발생하더라도 runner crash 가 아니라 report fail 로 남긴다.

## 비목표

- `TransportFactory.CreateDefault()` 기본 backend 를 RIO로 바꾸지 않는다.
- UDP receive prefetch, UDP reliability, 순서보장, 혼잡제어를 구현하지 않는다.
- latency hard gate, warning-as-failure, CI failure policy 를 승격하지 않는다.
- raw report schema-version 을 올리거나 summary/history schema 를 새로 만들지 않는다.
- repository baseline canonical directory 에 RIO UDP artifact 를 바로 채택하지 않는다.
- IPv6 UDP RIO 지원을 이번 작업에 포함하지 않는다.

## 설계 결정

### 1. 기존 runner 명령에 `--protocol <tcp|udp>` selector 를 추가한다

새 `--udp-load` 같은 명령을 만들지 않고, 기존 실행 명령에 additive option 을 둔다.

- `--smoke [--protocol <tcp|udp>] [--backend <saea|rio>] [--report <path>]`
- `--load [--protocol <tcp|udp>] [--backend <saea|rio>] [--report <path>]`
- `--load-open-loop [--protocol <tcp|udp>] [--backend <saea|rio>] [--report <path>]`
- `--baseline-suite <output-dir> [--runs <count>] [--protocol <tcp|udp>] [--backend <saea|rio>]`

기본값은 `tcp`다. 기존 command, script, baseline workflow 는 옵션을 생략하면 현재 TCP 동작을 유지한다.

summary/history command 는 이미 raw report 를 읽는 aggregate 단계이므로 `--protocol`을 받지 않는다.
summary/history 에 `--protocol`이 들어오면 `--backend`와 같은 usage error 로 처리한다.

### 2. raw report schema 는 유지하고 profile/scenario 로 구분한다

UDP result 도 기존 `TcpLoopbackRunResult`/writer schema 를 재사용한다. 타입 이름은 TCP 중심이지만 field 자체는
payload, target rate, sent/received/drop, TCP/UDP HWM, latency, pool leak, identity 로 구성되어 UDP에도 맞다.
이번 단위에서 schema 안정성을 위해 public/raw JSON field 를 넓히지 않는다.

권장 identity/scenario 값:

- SAEA UDP
  - `benchmark-profile`: `udp-loopback-saea-v1`
  - `transport-backend`: `SaeaTransport`
  - `scenario`: `udp-loopback-saea-baseline`, `udp-loopback-saea-baseline-smoke`, `udp-loopback-saea-baseline-open-loop`
- RIO UDP
  - `benchmark-profile`: `udp-loopback-rio-v1`
  - `transport-backend`: `RioTransport`
  - `scenario`: `udp-loopback-rio-baseline`, `udp-loopback-rio-baseline-smoke`, `udp-loopback-rio-baseline-open-loop`

summary/history comparison key 는 `benchmark-profile`, `transport-backend`, workload cases 를 이미 본다.
따라서 TCP/UDP report 를 같은 directory 에 섞으면 comparison mismatch 로 드러나며, 별도 schema field 없이 혼합을 감지할 수 있다.

### 3. UDP scenario 는 BrokerServer UDP self-command path 를 그대로 사용한다

runner 는 `BrokerServer.StartUdpAsync(...)`로 server UDP endpoint 를 시작한다.

흐름:

1. subscriber UDP socket 을 loopback local port 에 bind 한다.
2. publisher UDP socket 을 loopback local port 에 bind 한다.
3. subscriber socket 이 server endpoint 로 `SUBSCRIBE <topic>` datagram 을 보낸다.
4. 기존 TCP runner 와 같은 방식으로 subscription count 가 1이 될 때까지 대기한다.
5. publisher socket 은 server endpoint 로 `PUBLISH <topic> <payload>` datagram 을 보낸다.
6. subscriber socket 은 fan-out 된 raw payload datagram 을 받는다.

UDP fan-out payload 는 TCP subscriber outbound 와 달리 frame 이 아니므로 receiver 는 datagram payload 전체를 바로 검증한다.
latency timestamp, sequence, payload pattern layout 은 TCP runner 의 payload layout 을 그대로 사용한다.

### 4. closed-loop 와 open-loop 의미를 TCP와 맞춘다

closed-loop load:

- 각 publish datagram 후 subscriber receive 를 기다린다.
- delivery/drop/leak hard gate 를 빠르게 검증하는 안정성 경로다.

open-loop:

- subscriber receive task 를 먼저 시작한다.
- publisher 는 100Hz schedule 만 보고 publish datagram 을 보낸다.
- receive timeout 이 발생하면 runner 를 예외로 죽이지 않고 `received < sent` 또는 `payload-errors > 0`인 failed report 를 남긴다.

D111 기준으로 RIO UDP는 handler blocked-window datagram retention 을 보장하지 않는다.
open-loop 결과에서 loss 가 나오면 그것은 benchmark evidence 이며, runner 가 조용히 멈추거나 crash 해서는 안 된다.

### 5. artifact 는 먼저 scratch 로 수집한다

구현 후 첫 수집 위치는 repository baseline 이 아니라 scratch 영역이다.

예:

```text
artifacts/benchmarks/rio-udp/2026-06-26/session-01/
  saea-load.json
  saea-open-loop.json
  rio-load.json
  rio-open-loop.json
  mixed-summary.json
  mixed-summary.md
```

`artifacts/`는 이미 repository baseline 이 아닌 임시 evidence 영역으로 취급한다.
RIO UDP artifact 를 `docs/benchmarks/baselines/` 아래 canonical baseline 으로 채택할지는 별도 리뷰 이후 결정한다.

## 구현 순서

### Task 1 — CLI protocol selector model/parser

- `LoopbackProtocol` 또는 동등한 internal enum 을 추가한다.
- `BenchmarkCommandLine`이 protocol 을 보존한다.
- runner/baseline-suite command 에서 `--protocol <tcp|udp>`를 파싱한다.
- summary/history/help/target 에서 `--protocol`을 usage error 로 막는다.
- Red: `--load --protocol udp --backend rio --report out.json`이 protocol 을 보존하지 못하는 assertion failure.
- 검증: `BenchmarkCommandParserTests` focused run.

### Task 2 — protocol-aware runner dispatch

- Program wiring 에서 protocol 을 보고 TCP runner 또는 UDP runner 로 dispatch 한다.
- baseline-suite 는 같은 protocol 로 load/open-loop 를 반복 수집한다.
- usage text 에 protocol option 을 반영한다.
- Red: Program smoke/report test 또는 runner dispatch test 에서 UDP protocol 이 TCP runner 로 흘러가는 assertion failure.
- 검증: focused benchmark tests.

### Task 3 — UDP loopback scenario runner

- UDP socket helper, SUBSCRIBE/PUBLISH datagram builder, datagram receive helper 를 추가한다.
- SAEA UDP smoke/closed-loop/open-loop 를 먼저 green 으로 만든다.
- RIO backend 는 기존 backend selector 를 그대로 재사용하되 unavailable 환경은 explicit execution failure 로 둔다.
- Red: UDP smoke 가 기존 runner 부재로 report 를 만들지 못하거나 delivery assertion 에 실패.
- 검증: SAEA UDP smoke CLI, focused benchmark tests.

### Task 4 — RIO UDP artifact 수집과 상태 문서 갱신

- RIO available 환경에서 `--load --protocol udp --backend rio --report ...`와
  `--load-open-loop --protocol udp --backend rio --report ...`를 scratch 로 수집한다.
- 필요하면 SAEA UDP 같은 workload 를 같은 scratch session 에 수집해 비교한다.
- mixed summary 는 mismatch 를 expected evidence 로 기록하되 hard gate 와 warning policy 는 바꾸지 않는다.
- 검증: RIO UDP report의 `benchmark-profile`, `scenario`, `transport-backend`,
  `udp-pending-send-queue-high-watermark`, delivery/drop/leak field 확인.

## failure mode

### RIO UDP open-loop loss

- Trigger: receive handler 또는 send queue pressure 때문에 outstanding UDP receive 가 없는 window 에 datagram 이 들어온다.
- Impact: `received < sent`, payload error, 또는 UDP HWM/drop 증가가 report 에 남을 수 있다.
- Detection: raw report hard gate fail, summary `hard-passed=false`.
- Mitigation: runner 는 예외로 중단하지 않고 report 를 남긴다. bounded receive prefetch 는 별도 설계 후보로 둔다.

### TCP/UDP artifact 혼합

- Trigger: 같은 summary input directory 에 TCP와 UDP raw report 를 섞는다.
- Impact: 같은 baseline 으로 비교하면 안 되는 결과가 한 summary 에 들어간다.
- Detection: `benchmark-profile`/`scenario` 차이로 comparison mismatch 가 발생한다.
- Mitigation: 정상 수집은 protocol/backend 별 output directory 를 분리한다. 혼합 summary 는 비교 evidence 용으로만 사용한다.

### benchmark client-side command allocation

- Trigger: UDP PUBLISH datagram 을 만들 때 command prefix 와 payload 를 하나의 client-side byte array 로 구성한다.
- Impact: benchmark client 에 할당이 생긴다.
- Detection: production transport pool leak 지표에는 반영되지 않는다.
- Mitigation: 이 할당은 benchmark publisher 입력 생성 비용이며 production Broker/Transport hot path 가 아니다. runner 내부에서는 buffer 재사용으로 불필요한 반복 할당을 줄인다.

## 검증 계획

- `dotnet test tests\Hps.Benchmarks.Tests\Hps.Benchmarks.Tests.csproj --no-restore`
- `dotnet run --project tests\Hps.Benchmarks\Hps.Benchmarks.csproj -- --smoke --protocol udp --backend saea --report <temp>`
- RIO available 환경:
  - `dotnet run --project tests\Hps.Benchmarks\Hps.Benchmarks.csproj -- --smoke --protocol udp --backend rio --report <temp>`
  - `dotnet run --project tests\Hps.Benchmarks\Hps.Benchmarks.csproj -- --load --protocol udp --backend rio --report <temp>`
  - `dotnet run --project tests\Hps.Benchmarks\Hps.Benchmarks.csproj -- --load-open-loop --protocol udp --backend rio --report <temp>`
- `dotnet build HighPerformanceSocket.slnx --no-restore`
- `dotnet test HighPerformanceSocket.slnx --no-build`
- `git diff --check`

## 후속

- UDP runner 구현 뒤 RIO UDP scratch artifact 를 보고 default promotion backlog 를 재평가한다.
- RIO UDP open-loop loss 또는 HWM 증가가 반복되면 bounded receive prefetch 를 별도 설계로 승격한다.
- UDP artifact 를 repository baseline 으로 채택하려면 protocol-aware baseline directory convention 을 별도로 리뷰한다.
