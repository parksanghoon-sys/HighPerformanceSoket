# io_uring benchmark artifact workflow 설계

## 목적

D146으로 benchmark CLI 는 `--backend iouring`을 받을 수 있게 됐다.
하지만 현재 원격에서 실제 Linux `io_uring` benchmark raw report 를 만들 workflow 가 없다.

- `.github/workflows/benchmark-artifacts.yml`은 Windows runner 전용이며 기본 SAEA benchmark 만 실행한다.
- `.github/workflows/iouring-linux-contract.yml`은 Linux에서 `Hps.Transport.IoUring.Tests`만 실행하고 benchmark report 는 만들지 않는다.

따라서 다음 단위는 별도 opt-in GitHub Actions workflow 로 Linux available runner 에서 TCP/UDP
`--backend iouring` baseline suite 를 실행하고, 기존 raw report/summary/history schema 를 artifact 로 업로드하는 것이다.

## 결정

D147: `iouring-benchmark-artifacts.yml` workflow 를 추가한다.

이 workflow 는 D146 selector 를 원격 Linux runner 에서 검증하기 위한 수동 artifact 경로다.
기존 Windows benchmark workflow 와 분리하고, default backend promotion, fixed buffer registration, zero-copy send,
latency hard gate 는 열지 않는다.

## workflow 경계

- 파일: `.github/workflows/iouring-benchmark-artifacts.yml`
- trigger: `workflow_dispatch` 전용
- runner: `ubuntu-latest`
- timeout: 40분
- runner identity:
  - `HPS_BENCHMARK_RUNNER_ID=ci-linux-iouring-x64-01`
  - `HPS_BENCHMARK_RUNNER_KIND=ci`
- backend: 항상 `--backend iouring`
- protocol: TCP와 UDP를 모두 실행한다.
- runs: 각 protocol 별 `--runs 1`

`--runs 3` 대신 1회로 시작한다.
목적이 hard gate 가 아니라 첫 Linux io_uring benchmark evidence path 검증이므로, 비용과 timeout 위험을 낮춘다.
반복 baseline 채택은 artifact 검토 뒤 별도 결정으로 다룬다.

## artifact 구조

업로드 artifact 이름:

```text
iouring-benchmark-artifacts-<yyyy-mm-dd>-github-<run-id>-<attempt>
```

업로드 root:

```text
artifacts/benchmarks/runners/ci-linux-iouring-x64-01/<yyyy-mm-dd>/
```

내부 구조:

```text
tcp/session-01/{load-01.json,open-loop-01.json,summary.json,summary.md}
tcp/history.json
tcp/history.md
udp/session-01/{load-01.json,open-loop-01.json,summary.json,summary.md}
udp/history.json
udp/history.md
summary.md
dotnet-info.txt
```

TCP/UDP를 같은 date root 바로 아래 session 으로 섞지 않는다.
`BaselineHistoryReader`는 하나의 protocol/profile/backend 묶음만 읽어야 하므로 protocol 별 root 를 분리한다.

## 실행 순서

1. checkout
2. setup .NET 9
3. restore
4. build
5. `dotnet --info` 저장
6. TCP `--baseline-suite ... --protocol tcp --backend iouring --runs 1`
7. TCP summary/history 작성
8. UDP `--baseline-suite ... --protocol udp --backend iouring --runs 1`
9. UDP summary/history 작성
10. workflow summary 작성
11. artifact upload
12. 저장한 exit code 중 하나라도 non-zero 이면 job failure 로 되돌림

기존 Windows benchmark workflow 처럼 baseline 단계가 실패해도 summary/history/upload 는 가능한 범위에서 계속 실행한다.
단, 마지막 step 에서 실패를 job failure 로 복원한다.

## failure policy

workflow failure 로 본다.

- restore/build 실패
- baseline suite command non-zero
- summary/history command non-zero
- artifact upload 실패

workflow failure 로 보지 않는다.

- latency p99 값이 reference envelope 보다 큼
- warning count 가 0보다 큼
- reference history 가 아직 없음

이 workflow 는 evidence 수집 경로이며, hard latency gate 나 default promotion 판단이 아니다.

## 테스트 전략

- `BenchmarkArtifactWorkflowTests`에 새 workflow static shape test 를 추가한다.
- test 는 다음을 고정한다.
  - workflow 파일 이름과 `ubuntu-latest`
  - `workflow_dispatch` 전용
  - runner id `ci-linux-iouring-x64-01`
  - TCP/UDP 각각 `--baseline-suite`, `--protocol tcp|udp`, `--backend iouring`, `--runs 1`
  - TCP/UDP summary/history 작성
  - artifact upload 가 final failure gate 보다 먼저 실행됨
  - `actions/upload-artifact@v7.0.1`
- 실제 Linux benchmark 실행은 로컬 Windows에서 수행하지 않는다.
  사용자가 push 한 뒤 GitHub Actions artifact 로 검토한다.

## 제외 범위

- 기존 `benchmark-artifacts.yml`를 Linux matrix 로 확장
- push/pull_request/schedule trigger 추가
- reference envelope comparison 작성
- artifact 자동 채택
- latency hard gate 또는 warning-as-failure
- `TransportFactory.CreateDefault()` 변경
- fixed registration, zero-copy send, IPv6 direct io_uring UDP

## 다음 단계

1. workflow static Red test 를 추가한다.
2. `iouring-benchmark-artifacts.yml`를 추가한다.
3. focused workflow tests 와 solution build/test 를 실행한다.
4. D147 state docs 를 갱신한다.
5. commit 후 사용자가 push 하면 workflow 를 수동 실행하고 artifact 를 검토한다.
