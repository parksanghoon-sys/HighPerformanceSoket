# Sample Broker Explicit io_uring Transport 설계

## 목적

D231에서 `IoUringTransport`의 Linux native registered payload registration과 TCP `WRITE_FIXED` hit가 확인됐다.
D232 사용 가이드는 애플리케이션이 `IoUringTransport`를 `BrokerServer`에 직접 주입하는 opt-in 경로를 설명한다.
하지만 사용자가 바로 실행하는 `Hps.Sample.BrokerServer`의 `--transport` 값은 아직 `saea|rio|auto`만 지원한다.

이 설계의 목표는 sample broker CLI에 명시적 `--transport iouring` 모드를 추가해 Linux 사용자가
현재 검증된 io_uring TCP broker 경로를 직접 실행할 수 있게 하는 것이다.

## 핵심 결정

- 새 mode는 explicit `iouring`만 추가한다.
- 기존 `auto`는 RIO available 시 RIO, 그 외 SAEA라는 현재 의미를 유지한다.
- `TransportFactory.CreateDefault()`는 계속 `SaeaTransport`를 반환한다.
- explicit `iouring`이 unavailable이면 SAEA로 fallback하지 않고 exit code 1로 실패한다.
- sample broker는 현재와 같이 TCP listener만 연다. UDP sample CLI는 이번 범위에 포함하지 않는다.
- Linux workflow는 io_uring test project와 sample broker project만 명시적으로 restore/build한다.
  solution 전체 restore/build와 WPF `EnableWindowsTargeting`은 계속 제외한다.

## 현재 구조

### CLI parsing

- `SampleTransportMode`는 `Saea`, `Rio`, `Auto`를 가진다.
- `SampleBrokerServerCommandParser`는 positional argument 3개와 optional
  `--transport <saea|rio|auto>`를 해석한다.
- parser는 transport availability를 판단하지 않고 사용자의 mode만 `SampleBrokerServerCommandLine`에 보존한다.

### Transport selection

- `SampleTransportSelector`는 capability probe와 transport factory를 delegate로 주입받는다.
- `saea`는 probe 없이 SAEA를 생성한다.
- explicit `rio`는 RIO unavailable 또는 IPv6 listen 주소에서 fail-closed로 실패한다.
- `auto`는 RIO unavailable 또는 non-IPv4 listen 주소에서 SAEA로 fallback하고 notice를 남긴다.
- 이 selector 경계 덕분에 tests는 실제 OS와 native capability에 의존하지 않는다.

### Program composition

- `Program`은 parser 결과와 parsed address family를 selector에 전달한다.
- sample project는 현재 `Hps.Transport.Rio`를 참조하지만 `Hps.Transport.IoUring`은 참조하지 않는다.
- selection 성공 후 `BrokerServer`를 만들고 `StartTcpAsync(...)`로 listener를 시작한다.

### Linux contract workflow

- `.github/workflows/iouring-linux-contract.yml`은 현재
  `Hps.Transport.IoUring.Tests` project만 restore/build/test한다.
- `BenchmarkArtifactWorkflowTests`는 solution/WPF를 제외하기 위해 이 단일 project 경계를 정적으로 검증한다.
- D231 artifact의 native tests가 io_uring TCP receive/send와 registered payload fixed hit를 검증한다.

## 검토한 접근

### 접근 A: 기존 parser/selector에 explicit `iouring` 추가

권장안이다.

장점:

- 기존 sample host와 command 형태를 재사용한다.
- `BrokerServer` 조립 경계와 `IoUringTransport` public API를 그대로 사용한다.
- selector delegate injection 패턴으로 OS-independent unit test가 가능하다.
- 기존 `auto`와 default 정책을 바꾸지 않는다.

단점:

- selector full overload에 io_uring probe/factory가 추가된다.
- sample project가 OS-specific backend project 두 개를 참조하게 된다.

### 접근 B: Linux 전용 sample broker project 추가

별도 project에서 `IoUringTransport`를 직접 생성한다.

장점:

- 기존 sample selector를 건드리지 않는다.
- Linux 실행 명령이 명확하다.

단점:

- broker host lifecycle, argument parsing, usage text가 중복된다.
- SAEA/RIO/io_uring 공통 sample 흐름이 갈라져 유지보수 비용이 커진다.

판단: 제외한다.

### 접근 C: `auto`를 OS-aware RIO/io_uring/SAEA 선택으로 변경

Windows에서는 RIO, Linux에서는 io_uring, 그 외에는 SAEA를 자동 선택한다.

장점:

- 사용자는 하나의 `auto` mode만 알면 된다.

단점:

- 기존 `auto`의 RIO preferred 의미가 바뀐다.
- sample composition 단계의 default promotion에 가까우며 D231 correctness gate만으로는 근거가 부족하다.
- fallback 우선순위와 IPv4/IPv6 parity 정책을 다시 설계해야 한다.

판단: 이번 범위에서 제외한다.

## CLI 계약

### 허용 값

```text
--transport <saea|rio|iouring|auto>
```

- 값 비교는 기존 mode와 동일하게 대소문자를 구분하지 않는다.
- option 생략 시 기존과 같이 `Saea`다.
- `SampleTransportMode.IoUring`은 기존 enum numeric value를 바꾸지 않도록 마지막에 추가한다.

### 실행 예

```powershell
dotnet run --project samples\Hps.Sample.BrokerServer\Hps.Sample.BrokerServer.csproj -- 127.0.0.1 5000 65536 --transport iouring
```

성공 시 기존 startup line의 transport 값은 다음과 같다.

```text
transport=IoUringTransport
```

### 오류 메시지

parser message는 허용 값을 모두 표시한다.

```text
--transport 옵션에는 saea, rio, iouring 또는 auto 값이 필요합니다.
--transport 옵션은 saea, rio, iouring 또는 auto 값만 사용할 수 있습니다.
```

explicit `iouring` 선택에서 capability가 unavailable이면 다음 원칙을 따른다.

- `UnsupportedOperatingSystem`: Linux에서만 사용할 수 있다는 오류를 반환한다.
- `Unavailable`: 현재 환경에서 사용할 수 없고 probe status가 무엇인지 반환한다.
- exit code는 `SampleTransportSelector.RuntimeFailureExitCode`, 즉 1이다.
- explicit mode이므로 SAEA fallback과 fallback notice는 만들지 않는다.

## Selector 설계

### Full overload

Program이 사용할 full overload는 address family와 두 backend probe/factory를 모두 받는다.

```csharp
public static SampleTransportSelection Select(
    SampleTransportMode mode,
    AddressFamily listenAddressFamily,
    Func<RioCapabilityStatus> getRioStatus,
    Func<IoUringCapabilityStatus> getIoUringStatus,
    Func<ITransport> createSaea,
    Func<ITransport> createRio,
    Func<ITransport> createIoUring)
```

기존 4-argument/5-argument overload는 source compatibility를 위해 유지하고 full overload로 위임한다.
기존 overload는 io_uring status delegate를 `UnsupportedOperatingSystem`으로, factory를 호출 시 계약 오류를 내는 guard로 제공한다.
따라서 기존 overload에 `IoUring` mode를 직접 전달해도 factory를 호출하지 않고 명시 실패로 수렴한다.

### 분기 순서

```text
Saea
  -> createSaea only

IoUring
  -> getIoUringStatus
  -> Available: createIoUring
  -> otherwise: failure, no fallback

Rio / Auto
  -> existing IPv4-only RIO address-family policy
  -> getRioStatus
  -> existing explicit failure or auto fallback
```

선택되지 않은 backend probe/factory는 호출하지 않는다.
CLI cold path이므로 delegate injection 자체는 hot-path allocation 규칙의 대상이 아니다.

### Address family

sample broker는 TCP만 시작하고 `IoUringTransport` TCP v1은 `IPEndPoint`의 IPv4/IPv6 socket을 생성할 수 있다.
따라서 explicit `iouring` selector는 RIO의 IPv4-only guard를 재사용하지 않는다.
실제 OS/kernel availability는 io_uring capability probe와 transport start/listen 경계가 검증한다.

## Program 및 project wiring

- `Hps.Sample.BrokerServer.csproj`에 `Hps.Transport.IoUring` project reference를 추가한다.
- `Program`은 full selector overload에 다음 값을 전달한다.
  - `RioCapabilityProbe.GetStatus`
  - `IoUringCapabilityProbe.GetStatus`
  - `SaeaTransport`, `RioTransport`, `IoUringTransport` factories
- usage text와 예시에 `iouring`을 추가한다.
- selection 이후 `BrokerServer`, pool, listener, Ctrl+C shutdown 흐름은 변경하지 않는다.
- `SampleTransportSelection` public shape는 변경하지 않는다.

## Linux workflow 설계

`.github/workflows/iouring-linux-contract.yml`은 solution 전체가 아니라 다음 두 Linux-safe project만 명시적으로 다룬다.

```text
tests/Hps.Transport.IoUring.Tests/Hps.Transport.IoUring.Tests.csproj
samples/Hps.Sample.BrokerServer/Hps.Sample.BrokerServer.csproj
```

workflow 계약:

- 두 project를 restore한다.
- 두 project를 `--no-restore`로 build한다.
- native runtime tests는 기존 `Hps.Transport.IoUring.Tests`만 실행한다.
- sample broker에 별도 장기 실행 process smoke를 추가하지 않는다.
- job의 sample build step success가 Linux composition/build evidence다.
- artifact의 기존 `iouring-tests.trx`와 stdout은 native TCP/UDP/fixed payload runtime evidence다.
- solution restore/build와 `EnableWindowsTargeting`은 계속 금지한다.

`BenchmarkArtifactWorkflowTests`의 기존
`RestoresAndBuildsOnlyIoUringTestProject` 계약은 “명시한 Linux-safe 두 project만 restore/build”하는 계약으로 갱신한다.

## TDD 순서

프로덕션 코드 전에 assertion failure를 확인하며 compile failure를 Red로 사용하지 않는다.

### 1. Parser contract

- 문자열 `--transport iouring`을 넣고 parse success와 mode 보존을 기대하는 테스트를 먼저 추가한다.
- enum member가 아직 없어도 컴파일되도록 첫 Red는 parse boolean/error assertion으로 시작한다.
- Red 확인 후 `SampleTransportMode.IoUring`, parser mapping, 허용 값 message를 최소 구현한다.

### 2. Selector behavior

- io_uring `Available`이면 `IoUringTransport` factory만 호출한다.
- `UnsupportedOperatingSystem`과 `Unavailable`은 exit code 1로 실패하고 SAEA factory를 호출하지 않는다.
- explicit io_uring IPv6 address family는 RIO guard에 막히지 않는다.
- SAEA는 어느 native probe도 호출하지 않는다.
- RIO/Auto 기존 테스트는 모두 그대로 통과해야 한다.

### 3. Project/Program wiring

- sample csproj에 io_uring project reference가 없다는 assertion failure부터 시작한다.
- usage output에 `saea|rio|iouring|auto`가 없다는 assertion failure를 확인한다.
- reference, Program full selector wiring, usage text를 최소 구현한다.

### 4. Workflow contract

- workflow에 sample broker restore/build path가 없다는 assertion failure를 확인한다.
- 정적 workflow test와 YAML을 함께 갱신한다.
- io_uring tests만 runtime test한다는 기존 TRX 계약은 유지한다.

모든 새 test method 바로 위에는 무엇을 검증하고 왜 필요한지 설명하는 한국어 주석을 둔다.

## 검증 전략

### Local

- `Hps.Sample.BrokerServer.Tests` parser/selector/program/project tests
- `Hps.Benchmarks.Tests` workflow static contract tests
- sample broker project build
- solution build 경고 0/오류 0
- solution tests 전체 통과
- Windows에서 explicit `iouring` 실행 시 fail-closed exit code 1과 오류 message 수동 smoke
- 기존 SAEA sample broker/subscriber/publisher loopback 회귀 smoke

### Remote Linux

사용자 push 이후 `iouring-linux-contract.yml`을 실행한다.

확인 항목:

- workflow/job conclusion success
- sample broker restore/build steps success
- head SHA가 대상 commit과 일치
- artifact upload success
- `iouring-tests.trx` failed/error/timeout/aborted 0
- capability `Available`
- 기존 TCP loopback과 registered payload fixed-send hit evidence 유지

sample broker runtime process를 별도로 띄우지 않으므로, 이 gate는 다음 두 증거의 조합이다.

1. sample parser/selector tests와 Linux sample project build가 CLI composition을 검증한다.
2. 기존 io_uring native tests가 선택된 backend의 runtime data path를 검증한다.

## Material failure modes

### Explicit mode가 조용히 SAEA로 fallback

- Trigger: io_uring unavailable 환경에서 explicit mode 실행
- Impact: 사용자는 io_uring을 검증한다고 생각하지만 SAEA를 측정한다.
- Detection: selector test에서 selected backend/factory call을 검증한다.
- Mitigation: explicit mode는 항상 exit code 1 fail-closed다.

### `auto` 의미가 의도치 않게 변경

- Trigger: 공통 selector 분기 재정리 중 io_uring을 auto 후보에 포함
- Impact: 기존 Linux/Windows sample backend 선택과 benchmark 비교 기준이 바뀐다.
- Detection: 기존 Auto tests와 docs contract를 유지한다.
- Mitigation: io_uring 분기는 explicit mode에서만 진입한다.

### Linux workflow가 다시 solution/WPF restore로 확대

- Trigger: sample build를 추가하면서 solution build로 단순화
- Impact: `net9.0-windows` dashboard 때문에 Linux restore가 `NETSDK1100`으로 실패할 수 있다.
- Detection: static workflow contract test가 solution path와 `EnableWindowsTargeting` 부재를 검증한다.
- Mitigation: Linux-safe 두 csproj path만 명시한다.

### 선택되지 않은 capability probe 호출

- Trigger: selector가 모든 probe를 먼저 평가
- Impact: SAEA 실행도 불필요한 native probe 비용과 platform side effect를 가진다.
- Detection: fake probe call count tests
- Mitigation: mode 분기 안에서 필요한 probe만 호출한다.

## 제외 범위

- `auto`의 OS-aware io_uring 선택
- `TransportFactory.CreateDefault()` 변경
- WPF dashboard backend selector 추가
- UDP 전용 sample broker CLI
- io_uring UDP registered send pool
- registered payload hit/miss metric 추가
- zero-copy 또는 latency 개선 주장
- package/NuGet 배포

## 성공 기준

구현과 remote gate가 끝나면 다음을 주장할 수 있다.

- Linux 사용자는 sample broker CLI에서 `--transport iouring`을 명시할 수 있다.
- io_uring unavailable 환경에서는 다른 backend로 숨은 fallback 없이 명확히 실패한다.
- 기존 `saea`, `rio`, `auto` 동작은 유지된다.
- sample broker project는 Linux에서 io_uring reference를 포함한 상태로 build된다.
- 선택된 io_uring backend의 native runtime 경로는 기존 Linux contract evidence로 계속 검증된다.

아직 주장하지 않는 것:

- io_uring이 sample 또는 library default backend다.
- `auto`가 Linux에서 io_uring을 선택한다.
- sample broker가 UDP endpoint도 CLI로 연다.
- fixed payload 경로가 SAEA/RIO보다 빠르다.
- end-to-end zero-copy가 달성됐다.
