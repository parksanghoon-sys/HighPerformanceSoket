# Linux io_uring contract gate 설계

- 날짜: 2026-06-29
- 상태: Accepted for implementation planning
- 관련 결정: D133, D134, D135, D136, D137
- 관련 범위: `Hps.Transport.IoUring`, `Hps.Transport.IoUring.Tests`, GitHub Actions

## 배경

Phase 6 io_uring backend 는 project skeleton, capability probe, native wrapper, shared queue/completion loop,
TCP receive/send pump shape 까지 구현됐다. 다만 현재 작업 환경은 Windows 이므로 Linux `io_uring_setup`,
`io_uring_enter`, TCP receive/send loopback 은 실제 syscall 경로로 검증되지 않았다.

이 상태에서 UDP pump 나 fixed-buffer/zero-copy 최적화를 바로 추가하면 결함 위치가 흐려진다. TCP native 경로가
Linux host 에서 실제로 동작하는지 확인할 수 있는 gate 를 먼저 만들고, 그 다음 UDP/최적화 범위를 여는 것이
검증 가능성과 유지보수성 측면에서 더 안전하다.

## 목표

Linux host 에서 `Hps.Transport.IoUring.Tests`를 실행해 io_uring capability 상태와 TCP receive/send loopback 결과를
artifact 로 남기는 contract gate 를 만든다.

이 gate 는 다음을 보장한다.

- 기본 backend 선택은 계속 SAEA 로 유지한다.
- Linux `io_uring` capability 가 unavailable 이어도 workflow 자체를 실패로 보지 않는다.
- capability 가 available 인 host 에서는 기존 Linux-gated TCP receive/send loopback 이 실제 syscall 경로를 통과한다.
- 테스트 결과와 capability 판정 근거를 사람이 검토 가능한 artifact 로 남긴다.

## 후보 평가

### 후보 A — io_uring UDP pump 를 바로 설계/구현

장점은 Interface Server 의 TCP/UDP parity 로 바로 전진한다는 점이다. 하지만 TCP receive/send pump 의 실제 Linux
syscall 동작이 아직 검증되지 않았다. UDP를 더하면 completion loop, fd/socket lifetime, receive buffer lifetime,
endpoint close semantics 중 어느 층에서 문제가 생겼는지 분리하기 어렵다.

판단: 지금은 보류한다. TCP native evidence gate 이후에 다시 연다.

### 후보 B — fixed payload registration cache 또는 zero-copy send 최적화

장점은 성능 목표와 직접 연결된다는 점이다. 하지만 현재 단계에서는 correctness evidence 가 부족하고,
registration cache 는 `RefCountedBuffer` 수명, fan-out ownership, ring-wide fixed buffer table 정책을 동시에 건드린다.

판단: 보류한다. TCP/UDP correctness matrix 가 잡힌 뒤 성능 최적화로 다룬다.

### 후보 C — Linux io_uring contract gate

별도 Linux workflow 또는 manual host command 로 `Hps.Transport.IoUring.Tests`를 실행하고, 결과 artifact 를 남긴다.
workflow failure 조건은 build/test 실행 실패로 제한한다. capability unavailable 은 backend 미지원 상태를 보여주는
검증 결과이지 실패가 아니다.

판단: 채택한다. 현재 검증 공백을 가장 작게 줄이고 후속 UDP/최적화 작업의 기준선을 만든다.

## 설계

### Workflow 경계

새 workflow 는 기존 Windows benchmark workflow 와 분리한다.

- 파일: `.github/workflows/iouring-linux-contract.yml`
- trigger: `workflow_dispatch` 전용으로 시작한다.
- runner: `ubuntu-latest`
- 목적: Linux build/test 와 io_uring contract evidence 수집.
- failure: restore/build/test command 실패는 실패로 처리한다.
- non-failure: `IoUringCapabilityProbe.GetStatus()`가 `Unavailable`을 반환하는 경우는 실패가 아니다.

push trigger 는 첫 원격 실행 결과를 확인한 뒤 별도 결정으로 추가한다. 처음부터 push trigger 를 켜면 GitHub runner 의
kernel/seccomp 정책이 프로젝트 코드 변경과 무관한 noise 를 만들 수 있다.

### Artifact 경계

workflow 는 다음 artifact 를 업로드한다.

```text
artifacts/iouring/linux-contract/<yyyy-MM-dd>/run-<github-run-id>-<run-attempt>/
  dotnet-info.txt
  iouring-tests.trx
  summary.md
```

`summary.md`는 다음 정보를 포함한다.

- runner OS와 architecture
- `dotnet --version`
- 실행한 test command
- test exit code
- capability evidence test 가 남긴 output 확인 위치
- Linux actual loopback 검증은 `IoUringCapabilityStatus.Available` host 에서만 의미 있다는 주의

### Test evidence 경계

현재 Linux-gated loopback tests 는 capability unavailable host 에서 early-return 하므로, 단순 pass 만으로 실제
syscall loopback 이 수행됐는지 알 수 없다. 따라서 `Hps.Transport.IoUring.Tests`에 capability evidence test 를
추가한다.

이 테스트는 production behavior 를 바꾸지 않고 다음만 수행한다.

- `IoUringCapabilityProbe.GetStatus()`를 호출한다.
- status 가 `UnsupportedOperatingSystem`, `Unavailable`, `Available` 중 하나인지 검증한다.
- xUnit output 에 실제 status 를 남긴다.

이 output 은 TRX artifact 에 보존되어, 원격 실행 후 사람이 available/unavailable 상태를 구분할 수 있다.

### 결정

D138로 다음을 기록한다.

> io_uring 후속 구현은 UDP/zero-copy 최적화 전에 Linux contract evidence gate 를 먼저 둔다.

## 구현 계획 방향

1. capability evidence test 를 test-only hardening 으로 추가한다.
2. `iouring-linux-contract.yml` workflow 를 추가한다.
3. 상태 문서에 D138, 실행 방법, 원격 artifact 채택 기준을 기록한다.

## 검증 전략

로컬 Windows 검증:

- `dotnet test tests\Hps.Transport.IoUring.Tests\Hps.Transport.IoUring.Tests.csproj --no-build --no-restore -v minimal`
- `dotnet build HighPerformanceSocket.slnx --no-restore -v minimal`
- `dotnet test HighPerformanceSocket.slnx --no-build --no-restore -v minimal`
- `git diff --check`

원격 Linux 검증:

- GitHub Actions `iouring-linux-contract` workflow 를 수동 실행한다.
- artifact 의 `summary.md`와 `iouring-tests.trx`에서 capability status 와 test 결과를 확인한다.
- available host 에서 TCP receive/send loopback 이 실행되면 D137의 Linux actual verification backlog 를 완료 후보로 옮긴다.

## 범위 밖

- io_uring UDP pump 구현
- fixed buffer registration cache 또는 zero-copy send
- default backend promotion
- latency benchmark gate 또는 warning-as-failure
- GitHub push trigger 자동화
- Linux artifact 를 repository baseline 으로 자동 채택

## Self-review

- Placeholder scan: TBD/TODO 없이 구체적 workflow, artifact, test evidence 경계를 기록했다.
- Consistency: D137의 Windows-only 검증 한계를 D138 gate 로 해소하는 흐름이며, default SAEA 정책과 충돌하지 않는다.
- Scope: Linux contract evidence gate 로 제한하고 UDP/zero-copy/benchmark 승격은 분리했다.
- Ambiguity: capability unavailable 은 failure 가 아니라 evidence 상태라는 기준을 명시했다.
