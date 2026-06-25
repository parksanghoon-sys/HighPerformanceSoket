# CI baseline adoption 이후 Phase 4 재평가 설계

## 상태

Accepted.

## 배경

D095 절차에 따라 push-triggered GitHub Actions run `28145025444` artifact 를
`docs/benchmarks/baselines/runners/ci-windows-x64-01/2026-06-25/session-01/` 아래 repository baseline 으로 수동 채택했다.

채택된 CI baseline 은 다음 조건을 만족한다.

- raw report 6개를 포함한다.
- `summary.json`은 `hard-passed=true`, `warning-count=0`, `comparison-compatible=true`,
  `unknown-runner-count=0`이다.
- runner root `history.json`은 `session-count=1`, `hard-passed=true`,
  `warning-count=0`, `comparison-compatible=true`이다.
- runner metadata 는 `runner-id=ci-windows-x64-01`, `runner-kind=ci`,
  `benchmark-profile=tcp-loopback-saea-v1`, `transport-backend=SaeaTransport`로 일치한다.

## 판단

첫 CI repository baseline 은 Phase 4 artifact 체인의 end-to-end 동작을 증명하지만,
latency gate 또는 warning-as-failure 를 승격할 근거로는 부족하다.

이유는 다음과 같다.

- CI runner evidence 는 아직 date root 1개와 session 1개뿐이다.
- D082/D089의 gate 승격 기준은 runner identity 가 명확한 여러 date root 와 반복 session 을 요구한다.
- GitHub-hosted Windows runner 는 scheduling noise, VM placement, CPU quota 영향을 받기 쉽다.
- local runner 와 CI runner 의 latency envelope 는 비교군이 다르므로 같은 threshold 로 묶지 않는다.
- D090은 CI benchmark 를 artifact-only 단계로 시작한다고 정했고, D095는 repository baseline 채택을 수동 절차로 제한했다.

따라서 지금은 다음을 하지 않는다.

- latency hard gate 추가.
- warning-count 기반 CI 실패 승격.
- CI artifact 자동 repository baseline 채택.
- pull_request 또는 schedule trigger 추가.
- CI runner session 1개를 local runner reference envelope 와 합산.

## 결정

D096으로 다음 정책을 둔다.

1. 첫 CI baseline 채택 이후에도 Phase 4 benchmark gate 는 기존과 같이 delivery/drop/leak hard gate 만 유지한다.
2. CI latency/HWM/warning 값은 repository baseline index 와 history 에서 reference signal 로만 둔다.
3. CI runner baseline 은 자연스러운 push-triggered run 이 더 쌓일 때 D095 checklist 로 수동 채택 여부를 다시 판단한다.
4. CI runner gate 승격은 최소한 여러 date root 의 compatible, warning-free baseline 이 쌓인 뒤 별도 설계로 다룬다.
5. 추가 CI evidence 를 기다리는 동안 현재 실행 가능한 다음 큰 흐름은 Phase 5 Windows RIO backend 설계다.

## 다음 작업

다음 작업은 Phase 5 Windows RIO backend 의 첫 설계 단위로 넘긴다.

목표는 RIO 구현을 바로 시작하는 것이 아니라, 기존 `ITransport`/`TransportBase`/SAEA 기준선과
Phase 2/3 통합 테스트를 어떻게 재사용할지 먼저 확정하는 것이다.

초기 설계에서 다룰 범위는 다음과 같다.

- `src/Hps.Transport.Rio/` public/internal 책임 경계.
- Windows capability probe 와 `TransportFactory` 선택 정책.
- RIO P/Invoke surface 를 어느 수준까지 최소화할지.
- `PinnedBlockMemoryPool` block 등록과 수명 관리 책임.
- TCP 우선 구현 여부와 UDP/RIO send zero-copy 범위.
- SAEA 통합 테스트 재사용 방식과 Windows-only test skip 정책.
- Phase 4 benchmark 와의 비교 방식.

## 검증

이번 재평가는 문서 전용 작업이다.

- CI runner root history 와 session summary 를 대조한다.
- `docs/benchmarks/baselines/index.md`의 CI envelope 가 1-session reference 임을 명시한다.
- D082, D090, D095와 충돌하지 않는지 확인한다.
- `git diff --check`로 문서 변경의 whitespace 오류를 확인한다.
- 필요 시 solution build/test 로 문서 변경이 repo 상태를 깨지 않음을 확인한다.
