# CURRENT_PLAN.md - 현재 실행 지점

## 목표

- TCP에서 주 데이터 10,240B x 100 Hz 이상과 제어·관제 2,560B x 100 Hz를 동시에 처리한다.
- 두 stream은 topic과 TCP connection을 분리하고 exact delivery, bounded backpressure, 지연과 종료 누수를 재현 가능한 evidence로 판단한다.
- 기존 4096B x 100 Hz TCP/UDP baseline과 production Broker/Protocol/Transport 계약은 근거 없이 변경하지 않는다.

## 현재 확정 상태

- Phase 1~6의 메모리, SAEA, Protocol/Broker/Server, benchmark, RIO와 io_uring 경로는 구현되어 있다.
- D241 lifecycle/registration-pump 경합 보강은 solution 528/528와 Release build 경고 0/오류 0을 통과했다.
- D243은 기존 baseline을 보존하고 독립 TCP `--mixed-load-open-loop` command/report를 추가하는 방향으로 승인됐다.
- D243 written spec: `docs/superpowers/specs/2026-07-18-mixed-tcp-workload-gate-design.md`.
- D243 implementation plan: `docs/superpowers/plans/2026-07-18-mixed-tcp-workload-gate.md`.
- 2026-07-20 검토에서 fan-out latency 희석, 자원 preflight 부재와 publisher rate interval 오류를 확인해 spec/plan에 보완했다.
- D243 Task 2 `MixedWorkloadOptions`는 입력, checked 계획 수, subscriber 256명과 latency 저장소 128MiB 사전 검증까지 구현했다.
- D243 Task 3은 `N - 1` rate, worst-subscriber latency, 전역 zero gate, schema v2 mixed report와 backend별 identity까지 구현했다.
- D243 Task 4는 data/control 4개 TCP connection, pinned client buffer 재사용, 공통 absolute pacing과 단일 subscriber exact delivery runner까지 구현했다.
- D243 Task 5는 같은 runner를 고정 길이 subscriber collection으로 확장하고 subscriber별 exact delivery와 worst-latency 집계를 구현했다.
- D243 Task 6은 `--mixed-load-open-loop` CLI, 실행 전 자원 검증, Program runner/writer와 exit code를 연결했다.
- D243 Task 7은 Linux io_uring workflow에 mixed 전용 artifact root와 30초 3회 hard gate를 연결했다.
- D243 Task 8의 Windows SAEA/RIO 30초 각 3회와 SAEA 1,800초 soak는 모두 hard pass했다.
- pushed SHA `b7ffa22d80864d2c9e69fef1bac1dc6777efbfc1`의 workflow run `29802726026`은 project-scoped restore/build와 UDP baseline 6개를 통과했다.
- 같은 run의 TCP baseline은 첫 load/open-loop 뒤 두 번째 load에서 9분 이상 정지해 취소했고, mixed 3회는 exact delivery와 zero gate를 통과했지만 data p99 `5668.4~6791.6us`로 5ms hard gate를 모두 실패했다.
- D244로 io_uring TCP close 시 token별 `IORING_OP_ASYNC_CANCEL`, receive/send pump lifetime drain, 16KiB recv block과 benchmark command watchdog을 로컬 구현했다. 원격 수락은 아직 대기 중이다.

## 다음 단일 작업 단위

### D244 pushed-SHA Linux io_uring lifecycle·mixed evidence

- 사용자가 현재 D244 commit을 push한 뒤 `iouring-linux-contract.yml`과 `iouring-benchmark-artifacts.yml`을 같은 SHA에서 수동 실행한다.
- contract에서 peer-open pending recv 종료 test와 전체 native tests green을 확인한다.
- benchmark에서 TCP/UDP baseline exit 0, `IOURING_MIXED_EXIT=0`, mixed schema v2 report 3개 hard pass를 확인한다.
- mixed report가 legacy baseline summary/history/envelope source count에 섞이지 않았는지 확인한다.
- D244 push와 두 원격 artifact 확인 전에는 io_uring lifecycle·성능 수락을 완료로 기록하지 않는다.

## 최신 검증 기준

- D243 plan은 현재 benchmark parser, command line, Program, runner, identity, report reader/writer, endpoint diagnostics와 io_uring artifact workflow를 대조해 작성했다.
- 현재 `BaselineReportReader`는 `schema-version == 1` report를 legacy shape로 읽으므로 mixed report version 2 격리가 필요하다.
- 2026-07-21 현재 Task 5 focused tests 14/14, subscriber 1/2/duplicate integration 20회 반복과 benchmark tests 203/203이 통과했다.
- Release 단일-node build 경고 0/오류 0과 solution tests 613/613이 Task 5 최종 소스에서 통과했다.
- 2026-07-21 Task 6 parser/Program focused tests 50/50, benchmark tests 220/220과 benchmark Release build 경고 0/오류 0이 통과했다.
- SAEA 1초 CLI smoke는 data/control 100/100 exact delivery, drop/pending/pool/timeout 0과 schema v2 mixed report 생성을 확인했다.
- 2026-07-21 Task 7 workflow assertion Red를 확인한 뒤 focused tests 9/9와 benchmark tests 221/221이 통과했다.
- mixed workflow source는 전용 `mixed/<date>/session-01`에 report 3개를 수집하고 누적 exit를 기존 final gate에 포함한다.
- Task 8 source HEAD `cd1bd820450b9d9dc5f67baef19951af981ea033`에서 solution Release build 경고 0/오류 0과 tests 631/631이 통과했다.
- SAEA 30초 3회는 각 stream 3000/3000, 100.0Hz, 최악 p99 1480.0us, p999 2679.2us, HWM 최대 3과 전역·stream 오류 0이다. Raw: `artifacts/benchmarks/mixed/saea-20260721-124057/`.
- RIO 30초 3회는 실제 `RioTransport`로 각 stream 3000/3000, 100.0Hz, 최악 p99 1864.4us, p999 4052.3us, HWM 최대 3과 전역·stream 오류 0이다. Raw: `artifacts/benchmarks/mixed/rio-20260721-124300/`.
- SAEA 1,800초 soak는 각 stream 180000/180000, 100.0Hz, 최악 p99 1434.9us, p999 3105.8us, HWM 6과 drop/pending/pool/timeout/sequence/payload error 0이다. Raw: `artifacts/benchmarks/mixed/saea-soak-1800s.json`.
- raw report 7개를 schema, backend, count/min/max, rate/latency와 zero gate로 재검증했고 모두 `passed=true`다.
- `HPS_OPERATIONAL_DATA_RATE_HZ`와 `HPS_OPERATIONAL_SUBSCRIBERS`가 없어 별도 운영 fan-out run은 수행하지 않았다. 수락 범위는 100Hz/N=1이며 N=2는 기능 통합 검증만 보유한다.
- workflow run `29801941712`는 `75d81f54edea3930cf0fbffe266c2709acec07a6`을 checkout했지만 WPF sample이 포함된 solution restore에서 `NETSDK1100`으로 종료되어 benchmark는 실행되지 않았다.
- 회귀 테스트 Red를 확인한 뒤 Linux workflow restore/build 대상을 `tests/Hps.Benchmarks/Hps.Benchmarks.csproj`로 제한했다. benchmark build 경고 0/오류 0, benchmark tests 222/222와 solution tests 632/632가 통과했다.
- run `29802726026`은 SHA `b7ffa22d80864d2c9e69fef1bac1dc6777efbfc1`을 checkout했고 restore/build, UDP 6개와 artifact upload를 완료했다. TCP는 raw report 2개 뒤 정지했으며 mixed는 3회 모두 3000/3000, 100Hz, drop/pending/pool/timeout/error 0이지만 data p99 5ms를 초과했다.
- D244 Red는 pump reference cleanup 1건, receive task stop 관측 1건, 16KiB recv block 1건, async cancel queue/control/resource 연결 3건과 workflow watchdog 2건으로 확인했다.
- D244 Green은 io_uring tests 97/97, benchmark tests 223/223, solution tests 640/640과 Release 단일-node build 경고 0/오류 0을 통과했다.
- 전체 `dotnet format --verify-no-changes`는 이번 변경 밖 기존 whitespace 진단을 다수 포함해 실패했으며, `git diff --check`는 통과했다.
- 기본 병렬 build의 MSBuild worker 1개 종료와 VSTest 시작 timeout이 각각 한 번 있었으나 같은 소스의 단일-node build/test 재실행은 통과했고 코드 변경은 필요하지 않았다.
- 현재 SAEA TCP 4096B x 100 Hz x 30초 open-loop는 3000/3000, actual 99.8 Hz, p99 623.9us, HWM 5, drop/payload error/pool rented 0이다.
- RIO TCP smoke는 8/8, drop/payload error/pool rented 0이다.
- 이 검증은 legacy 단일 stream 기준선이며 mixed 10.24 Mbps evidence가 아니다.

## 구현 순서

1. [완료] options 입력 검증, checked 계획 수, subscriber/latency 저장 preflight.
2. [완료] `sent - 1` interval rate, worst-subscriber latency hard gate, typed report와 mixed run identity.
3. [완료] 단일 논리 구독자 mixed TCP runner.
4. [완료] N명 fan-out exact delivery와 subscriber별 latency 집계.
5. [완료] CLI와 Program 연결.
6. [완료] Linux io_uring mixed artifact workflow.
7. [부분 완료] SAEA/RIO 3회와 SAEA 1,800초 soak 완료. D244 push와 Linux contract/benchmark evidence 재실행 대기.

사용자가 남은 Task 전체 진행을 승인했으므로 각 단위는 D013에 따라 구현, 검증, 독립 review와 commit을 마친 뒤 다음 단위로 연속 진행한다.

## 유지할 범위 경계

- UDP 10,240B datagram, segmentation/reassembly, reliability는 현재 범위 밖이다.
- ACK/retry/durable delivery, topic priority, generic workload graph는 만들지 않는다.
- 실제 최대 data rate와 logical subscriber 수가 없으면 100Hz/N=1보다 큰 production capacity를 주장하지 않는다.
- production 변경 필요성이 보이면 mixed raw failure를 먼저 보존하고 별도 설계/review 단위로 분리한다.

## Archive

- 압축 전 전체 상태: `docs/agent-state/snapshots/2026-07-10-pre-compaction/`.
- 상세 변경 이력: `docs/agent-state/changelog/2026-07.md`.
- 상세 결정 이력: `docs/agent-state/decisions/2026-07.md`.
