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
- mixed code/tests와 10.24 Mbps 동시 workload 실행 evidence는 아직 없다.

## 다음 단일 작업 단위

### D243 mixed TCP workload implementation plan review stop

- 이번 cycle은 구현 계획과 상태 문서만 정렬했다. production code와 tests는 변경하지 않았다.
- 계획은 options/math, result/report, subscriber 1 runner, N명 fan-out, CLI, Linux workflow, 성능 evidence를 서로 다른 reviewable commit으로 나눈다.
- mixed JSON은 `schema-version: 2`를 사용해 version 1만 읽는 legacy baseline aggregate와 분리한다.
- 사용자 검토 승인 뒤 첫 구현 cycle은 plan Task 1 preflight와 Task 2 `MixedWorkloadOptions` TDD만 수행한다.
- Task 2 commit/review stop 전에는 result, runner, CLI를 함께 구현하지 않는다.

## 최신 검증 기준

- D243 plan은 현재 benchmark parser, command line, Program, runner, identity, report reader/writer, endpoint diagnostics와 io_uring artifact workflow를 대조해 작성했다.
- 현재 `BaselineReportReader`는 `schema-version == 1` report를 legacy shape로 읽으므로 mixed report version 2 격리가 필요하다.
- 기존 검증 기준은 solution tests 528/528, Release build 경고 0/오류 0이다. 이번 문서 cycle에서는 build/test를 재실행하지 않는다.
- 최근 SAEA/RIO 4096B x 100 Hz TCP/UDP run은 모두 3000/3000, drop/payload error/pool rented 0이다.

## 구현 순서

1. options 입력 검증과 checked 계획 수.
2. stream/global hard gate, schema-version 2 report와 mixed run identity.
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
