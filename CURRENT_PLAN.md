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
- mixed result/report, runner, CLI와 10.24 Mbps 동시 workload 실행 evidence는 아직 없다.

## 다음 단일 작업 단위

### D243 Task 2 `MixedWorkloadOptions` 구현 review stop

- reflection 계약 Red 1개와 behavior Red 10개를 확인한 뒤 options type과 검증 계산만 최소 구현했다.
- 기본 profile은 data/control 각각 3,000 message, 구독자 1명 기준 4개 client connection과 latency payload 72,000B로 계산한다.
- 모든 계획 수는 `checked long`으로 계산한 뒤 `int` 표현 범위, subscriber 256명과 latency payload 128MiB를 socket/배열 생성 전에 검증한다.
- options 생성자는 socket, payload buffer와 latency 배열을 만들지 않는다.
- 사용자 검토로 Task 2를 확정하기 전에는 Task 3 result/report를 시작하지 않는다.

## 최신 검증 기준

- D243 plan은 현재 benchmark parser, command line, Program, runner, identity, report reader/writer, endpoint diagnostics와 io_uring artifact workflow를 대조해 작성했다.
- 현재 `BaselineReportReader`는 `schema-version == 1` report를 legacy shape로 읽으므로 mixed report version 2 격리가 필요하다.
- 2026-07-20 현재 focused options tests 15/15, benchmark tests 133/133이 통과했다.
- Release build 경고 0/오류 0, solution tests 543/543이 통과했다.
- 현재 SAEA TCP 4096B x 100 Hz x 30초 open-loop는 3000/3000, actual 99.8 Hz, p99 623.9us, HWM 5, drop/payload error/pool rented 0이다.
- RIO TCP smoke는 8/8, drop/payload error/pool rented 0이다.
- 이 검증은 legacy 단일 stream 기준선이며 mixed 10.24 Mbps evidence가 아니다.

## 구현 순서

1. [완료] options 입력 검증, checked 계획 수, subscriber/latency 저장 preflight.
2. [다음 후보] `sent - 1` interval rate, worst-subscriber latency hard gate, typed report와 mixed run identity.
3. 단일 논리 구독자 mixed TCP runner.
4. N명 fan-out exact delivery.
5. CLI와 Program 연결.
6. Linux io_uring mixed artifact workflow.
7. SAEA/RIO 3회, 1,800초 soak, push된 SHA의 io_uring evidence.

각 단위는 D013에 따라 구현, 검증, 상태 문서, commit 후 사용자 review stop에서 멈춘다.

## 유지할 범위 경계

- UDP 10,240B datagram, segmentation/reassembly, reliability는 현재 범위 밖이다.
- ACK/retry/durable delivery, topic priority, generic workload graph는 만들지 않는다.
- 실제 최대 data rate와 logical subscriber 수가 없으면 100Hz/N=1보다 큰 production capacity를 주장하지 않는다.
- production 변경 필요성이 보이면 mixed raw failure를 먼저 보존하고 별도 설계/review 단위로 분리한다.

## Archive

- 압축 전 전체 상태: `docs/agent-state/snapshots/2026-07-10-pre-compaction/`.
- 상세 변경 이력: `docs/agent-state/changelog/2026-07.md`.
- 상세 결정 이력: `docs/agent-state/decisions/2026-07.md`.
