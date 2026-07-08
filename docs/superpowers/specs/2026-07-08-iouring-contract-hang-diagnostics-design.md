# D212 이후 io_uring contract hang diagnostics 설계

## 상태

Accepted.

## 배경

D210에서 TCP payload production path 를 `IORING_OP_WRITE_FIXED`로 직접 연결했지만,
원격 `iouring-linux-contract.yml` run `28907016232`에서 `Run io_uring tests` 단계가 20분 동안 끝나지 않았다.
GitHub Actions 는 test step 을 `cancelled`로 종료했고, artifact 에는 `summary.md`와 `dotnet-info.txt`만 남았다.
`iouring-tests.trx`는 생성되지 않았고, 어떤 테스트가 마지막으로 실행 중이었는지 확인할 수 없었다.

D211 rollback 이후 run `28908440081`은 success 로 복귀했다.
따라서 현재 baseline 은 안전하지만, fixed-write production 재시도 전에 원격 contract 자체가 hang 을 짧고 해석 가능한 실패로
수렴시키는 진단 경계가 필요하다.

## 목표

다음 구현 단위는 **io_uring Linux contract hang diagnostics gate**로 둔다.

목표는 production code 를 바꾸는 것이 아니라, 다음 native regression 이 hang 을 만들 때 다음 정보를 artifact 에 남기게 하는 것이다.

- hang 발생 시 workflow 20분 timeout 전 `dotnet test`가 자체 종료한다.
- xUnit/VSTest blame sequence 또는 diagnostic log 가 artifact 에 남는다.
- `summary.md`가 timeout/hang 여부와 exit code 를 명확히 기록한다.
- 정상 green run 에서는 기존 test count 와 필수 evidence 검토가 계속 가능하다.

## 후보 비교

### 후보 A: 바로 registration lifetime 설계를 작성하고 fixed-write 재시도

장점:

- D210 실패의 근본 원인 후보인 active queue registration lifetime 으로 빠르게 들어갈 수 있다.
- production 성능 목표에 더 직접적으로 연결된다.

단점:

- D211처럼 다시 hang 이 나면 어떤 테스트/단계에서 멈췄는지 알 수 없다.
- 원격 artifact 가 TRX 없이 끝나면 같은 분석 비용을 반복한다.

판단: 지금 바로 선택하지 않는다.

### 후보 B: workflow 전체 timeout 을 줄이기

장점:

- 실패 감지가 빠르다.
- 변경량이 작다.

단점:

- 여전히 TRX/sequence 없이 cancelled 될 수 있다.
- timeout 원인을 테스트 수준으로 좁히지 못한다.

판단: 단독으로는 부족하다.

### 후보 C: `dotnet test` blame-hang 진단을 contract workflow 에 추가

장점:

- `dotnet test --help` 기준 `--blame-hang-timeout`, `--blame-hang-dump-type none`, `--diag`가 지원된다.
- test process 가 workflow timeout 전에 종료되므로 summary/artifact upload 단계가 실행될 가능성이 높다.
- dump 를 `none`으로 두면 artifact 크기를 크게 늘리지 않고 sequence/diag 중심으로 원인을 남길 수 있다.
- production code 를 건드리지 않고 다음 native 재시도 전 관측성을 보강한다.

단점:

- blame sequence 파일 위치를 artifact root 에서 안정적으로 수집하도록 workflow 를 조금 더 다듬어야 한다.
- hang 원인을 자동으로 고치지는 않는다.

판단: 다음 구현 단위로 채택한다.

## 결정

다음 단위는 **D213 io_uring Linux contract hang diagnostics**다.

구현 방향:

1. `.github/workflows/iouring-linux-contract.yml`의 `dotnet test` command 에 hang 진단 옵션을 추가한다.
   - `--blame-hang`
   - `--blame-hang-timeout 2m`
   - `--blame-hang-dump-type none`
   - `--diag "$IOURING_CONTRACT_ROOT/vstest-diag.log"`
2. workflow summary 에 다음 값을 기록한다.
   - test exit code
   - artifact root
   - hang diagnostics enabled 여부
   - blame hang timeout 값
3. artifact upload 는 기존처럼 `if: always()`를 유지한다.
   - TRX가 없어도 summary와 diag가 남아야 한다.
   - blame sequence 파일이 생성되면 artifact root 아래에 포함되게 한다.
4. production `IoUringTransport` send path 는 이번 단위에서 변경하지 않는다.
   - TCP payload 는 rollback 된 `TrySubmitSend` baseline 을 유지한다.
5. local validation 은 workflow text/static contract test 와 project/solution test 로 수행한다.
   - 실제 hang 진단은 다음 원격 workflow run 에서 확인한다.

## 테스트 전략

### Red/Green 단위 1: workflow hang diagnostics contract

목표:

- workflow 가 `dotnet test`를 blame-hang 옵션과 diag log 로 실행하는지 static test 로 고정한다.
- summary 가 hang diagnostics 설정을 기록하는지 검증한다.

검증 예:

- `.github/workflows/iouring-linux-contract.yml` 텍스트에 `--blame-hang-timeout 2m`이 없으면 실패하는 test 를 추가한다.
- `--blame-hang-dump-type none`이 없으면 실패하게 한다.
- `vstest-diag.log`가 artifact root 에 기록되도록 `--diag "$IOURING_CONTRACT_ROOT/vstest-diag.log"` 존재를 검증한다.
- `summary.md` 생성 block 에 hang diagnostics enabled 문구가 있는지 검증한다.

### Red/Green 단위 2: local verification

목표:

- workflow static contract test 와 기존 io_uring tests 가 Windows/local 에서 통과한다.
- solution build/test 가 깨지지 않는다.

검증:

- focused workflow contract test
- `dotnet test tests/Hps.Transport.IoUring.Tests/Hps.Transport.IoUring.Tests.csproj -v minimal`
- `dotnet build HighPerformanceSocket.slnx -v minimal`
- `dotnet test HighPerformanceSocket.slnx -v minimal`
- `git diff --check`

### Red/Green 단위 3: remote contract gate

목표:

- D213 변경 이후 정상 baseline 에서 원격 `iouring-linux-contract.yml`이 success 로 유지되는지 확인한다.
- artifact 에 `summary.md`, `dotnet-info.txt`, `iouring-tests.trx`, `vstest-diag.log`가 포함되는지 확인한다.

검증:

- workflow/job conclusion success
- summary test exit code 0
- TRX counters failed 0
- 필수 native evidence tests Passed
- artifact 에 diag log 존재

## 범위 제외

- TCP payload fixed-write production 재연결
- registration cache 또는 queue/transport lifetime registration 구현
- zero-copy send
- UDP fixed-buffer send
- default backend promotion
- latency hard gate

## 리스크와 완화

- 리스크: blame-hang timeout 이 너무 짧아 느린 정상 native test 를 잘못 중단할 수 있다.
  - 완화: 기존 green run 은 20~30초 수준이고, 실제 D211 hang 은 20분까지 지속됐다. 2분은 정상/비정상 구분에 충분한 여유다.
- 리스크: dump 를 수집하면 artifact 가 커질 수 있다.
  - 완화: `--blame-hang-dump-type none`으로 sequence/diag 중심 evidence 만 남긴다.
- 리스크: diag log 가 너무 verbose 할 수 있다.
  - 완화: artifact retention 은 기존 14일이며, 실패 분석용으로만 사용한다.

## 다음 단계

D213 구현 계획을 작성한다.
계획은 workflow static contract test, workflow 변경, remote artifact 검토 문서화의 세 task 로 나눈다.
