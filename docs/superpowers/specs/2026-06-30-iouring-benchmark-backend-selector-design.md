# io_uring benchmark backend selector 설계

## 목적

D144로 io_uring UDP bounded receive window 가 Linux available runner 에서 동작한다는 contract artifact 는 확보했다.
하지만 아직 `4096 bytes x 100 Hz` 목표에서 io_uring backend 가 어느 정도의 delivery/drop/latency 상태인지
raw benchmark artifact 로 볼 수 없다.

따라서 다음 단위는 fixed payload registration cache, zero-copy send, IPv6 direct UDP, default backend promotion 을
바로 여는 것이 아니라, 기존 benchmark CLI 에 `--backend iouring`을 추가해 TCP/UDP loopback 성능 artifact 를
같은 schema 로 남기는 것이다.

## 현재 확인한 사실

- `tests/Hps.Benchmarks`의 실행 CLI 는 이미 `--protocol <tcp|udp>`와 `--backend <saea|rio>`를 가진다.
- `TcpLoopbackScenarioRunner`와 `UdpLoopbackScenarioRunner`는 backend enum 을 받아 `SaeaTransport` 또는
  `RioTransport`를 만든다.
- raw report identity 는 `benchmark-profile`, `transport-backend`, runner/environment metadata 를 이미 기록한다.
- `Hps.Benchmarks`는 아직 `Hps.Transport.IoUring`을 참조하지 않으므로 `IoUringTransport`를 생성할 수 없다.
- D144 artifact 는 native UDP receive/send/diagnostics/bounded window tests 를 통과했지만 benchmark run 은 아니다.

## 후보 비교

### 후보 A: fixed payload registration cache

지금 바로 열지 않는다.
send payload 는 broker fan-out 이 소유한 `RefCountedBuffer`이고, 임의 subscriber queue 와 in-flight lifetime 을 가진다.
io_uring ring-wide fixed buffer table 에 payload 를 cache 하려면 buffer identity, eviction, in-flight deregister 금지,
multi-endpoint sharing 정책을 먼저 설계해야 한다.

### 후보 B: receive fixed buffer registration

지금 바로 열지 않는다.
D143 receive loop 는 handler 가 datagram 을 잡고 있어도 같은 receive slot 을 즉시 repost 하기 위해 새
`RefCountedBuffer`를 다시 대여한다. slot 당 고정 buffer 1개를 등록하면 repost 와 handler ownership 이 충돌한다.
registered receive buffer slab 을 따로 만들려면 `RefCountedBuffer` 반환 경계나 pool lease model 을 확장해야 하므로
benchmark evidence 없이 먼저 넣기에는 범위가 크다.

### 후보 C: IPv6 direct io_uring UDP

지금 바로 열지 않는다.
현재 io_uring UDP v1은 D140에 따라 IPv4 direct path 로 제한되어 있고, default backend promotion 도 열려 있지 않다.
IPv6는 compatibility 확장이지 현재 성능 병목 확인의 첫 단계가 아니다.

### 후보 D: default backend promotion

지금 바로 열지 않는다.
D110/D119 계열 결정과 같이 기본 factory 승격은 contract matrix, fallback policy, benchmark artifact 가 충분히 쌓인 뒤 판단한다.
io_uring은 아직 opt-in backend 이며 Linux 전용이다.

### 선택: benchmark backend selector

`--backend iouring`을 추가한다.
기존 benchmark runner, raw JSON schema, summary/history/envelope toolchain 을 재사용하므로 새 artifact pipeline 을 만들지 않는다.
Linux available 환경에서만 실제 실행되고, Windows/non-Linux 에서는 명시적 `NotSupportedException`으로 빠르게 실패한다.

## 설계

### CLI

다음 명령에서 backend 값으로 `iouring`을 허용한다.

- `--smoke [--protocol <tcp|udp>] [--backend <saea|rio|iouring>] [--report <path>]`
- `--load [--protocol <tcp|udp>] [--backend <saea|rio|iouring>] [--report <path>]`
- `--load-open-loop [--protocol <tcp|udp>] [--backend <saea|rio|iouring>] [--report <path>]`
- `--baseline-suite <output-dir> [--runs <count>] [--protocol <tcp|udp>] [--backend <saea|rio|iouring>]`

summary/history/envelope 명령은 raw artifact 소비 단계이므로 기존처럼 `--backend`를 받지 않는다.

### identity

새 profile/backend 이름은 다음으로 고정한다.

- TCP profile: `tcp-loopback-iouring-v1`
- UDP profile: `udp-loopback-iouring-v1`
- transport-backend: `IoUringTransport`
- scenario base:
  - TCP: `tcp-loopback-iouring-baseline`
  - UDP: `udp-loopback-iouring-baseline`

이 값들은 SAEA/RIO artifact 와 comparison key 가 섞이지 않게 하는 장치다.

### transport 생성

`Hps.Benchmarks`가 `Hps.Transport.IoUring` project reference 를 가진다.
scenario runner 의 transport factory 는 `TcpLoopbackTransportBackend.IoUring`이면 다음을 확인한다.

- OS가 Linux 인가
- `IoUringCapabilityProbe.GetStatus() == IoUringCapabilityStatus.Available` 인가

조건을 만족하면 `new IoUringTransport()`를 반환한다.
조건을 만족하지 않으면 `NotSupportedException`을 던진다.
이는 RIO backend 의 현재 opt-in 실패 정책과 대칭이다.

### CI/원격 artifact

이번 구현은 benchmark workflow 를 새로 만들지 않는다.
먼저 local parser/report path 와 Linux-gated manual 실행 가능성을 연다.
사용자가 push 한 뒤 필요한 경우 기존 GitHub Actions 체인에 별도 io_uring benchmark artifact workflow 를 붙일지 판단한다.

## 테스트

- parser test: `--backend iouring`이 smoke/load/open-loop/baseline-suite 에서 인식된다.
- parser test: invalid backend 메시지가 `saea|rio|iouring` 기준으로 갱신된다.
- identity test: TCP/UDP io_uring profile 과 `IoUringTransport` backend 이름이 기록된다.
- scenario shape test: TCP/UDP runner 가 `IoUring` enum 값에 대해 io_uring transport factory branch 를 가진다.
- Program help test 또는 usage scan 으로 help text 의 backend 목록을 갱신한다.
- 실제 Linux native benchmark 실행은 Windows local 에서 수행하지 않는다. 이후 원격 artifact 로 확인한다.

## 제외 범위

- fixed buffer registration 구현
- `SEND_ZC`/`MSG_ZEROCOPY`
- `TransportFactory.CreateDefault()` 변경
- IPv6 direct io_uring UDP
- benchmark hard gate 또는 latency SLO 승격
- CI benchmark workflow 자동 추가

## 결정

D145: D144 이후 다음 구현 단위는 io_uring benchmark backend selector 다.
목표는 최적화 전 raw benchmark artifact 수집 경로를 여는 것이며, default promotion 이나 zero-copy 구현의 선행 조건으로 취급한다.
