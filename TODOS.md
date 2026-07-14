# TODOS.md

## Current TODOs

- 현재 실행 가능한 로컬 TODO 없음.

## Deferred Backlog

- [ ] `P1_SOON` 현재 local commit을 push하고 explicit io_uring 원격 Linux gate를 갱신한다.
  - 남은 일: `master`의 미푸시 local commit을 원격에 반영하고 io_uring workflow 결과를 확인한다.
  - 이유: 사용자가 현재 push할 수 없다고 명시해 로컬 구현과 분리했다.
  - 목적: 원격 branch 동기화와 Linux native backend 계약이 현재 checkout에서도 유지됨을 확인한다.
  - 관련 범위: git remote, `.github/workflows/iouring-linux-contract.yml`, 원격 TRX와 artifact. 새 production 변경은 기본 범위가 아니다.
  - 현재 상태: Windows solution 520/520과 RIO UDP depth 4 반복 gate는 통과했고 local commit으로 보존돼 있다.
  - blocker: 사용자의 push 가능 시점과 원격 workflow 실행 환경.
  - 다음 단계: 사용자가 push한 뒤 workflow의 project build, TRX total/executed/passed, failure/error/timeout,
    registered payload native evidence를 직접 확인한다.

- [ ] `P2_LATER` RIO full IPv6는 default promotion scope가 열릴 때 재평가한다.
  - 남은 일: RIO TCP/UDP는 IPv4 전용이고 sample `auto`는 non-IPv4에서 SAEA fallback을 사용한다.
  - 이유: 현재 fallback으로 기능이 유지되며 4096B x 100 Hz RIO evidence도 IPv4 기준이다.
  - 목적: IPv6 registered socket, sockaddr, scope id, dual-mode 정책과 benchmark evidence를 완성한다.
  - 범위: `Hps.Transport.Rio`, RIO tests, explicit RIO benchmark path.
  - 다음 단계: default promotion 요구가 생길 때 full implementation과 fallback 유지안을 비교한다.

- [ ] `P3_NICE` 실제 host/metrics consumer가 생기면 server-level diagnostics model을 설계한다.
  - 남은 일: `BrokerServer` 위의 운영 diagnostics API는 아직 없다.
  - 이유: 현재 server는 단일 `ITransport`를 감싼 얇은 host이고 transport diagnostics로 요구를 충족한다.
  - 목적: 구체적인 exporter 또는 server-only consumer가 생긴 뒤 필요한 aggregation만 추가한다.
  - 범위: `Hps.Server`, `Hps.Transport`, host/sample code.
  - 다음 단계: 실제 소비자 요구를 먼저 확보한다.

- [ ] `P3_NICE` explicit io_uring workflow의 exact command allow-list test는 필요성이 확인될 때만 추가한다.
  - 남은 일: Linux workflow의 모든 `dotnet restore/build/test` command를 exact set으로 고정하는 test가 없다.
  - 이유: D236 gate는 통과했고 현재 결함이 아니라 미래 scope-regression 방지 제안이다.
  - 목적: workflow scope가 반복적으로 넓어지는 문제가 재발할 때 최소 정적 계약으로 제한한다.
  - 범위: `BenchmarkArtifactWorkflowTests.cs`.
  - 다음 단계: 실제 regression 또는 workflow 변경 요구가 생길 때 assertion Red로 시작한다.

## Completed

- [x] 2026-07-14 RIO UDP depth 4 hardening 구현 review stop을 사용자 진행 승인으로 닫았다.
  - 다음 로컬 구현을 자동으로 열지 않고 push 및 explicit io_uring 원격 gate를 deferred backlog로 분리했다.
  - IPv6, server diagnostics와 workflow allow-list는 trigger가 없어 현재 우선순위에 유지했다.
- [x] 2026-07-14 RIO UDP fixed depth 4 hardening을 TDD와 반복 gate로 수락했다.
  - blocked-handler burst와 close-owner test가 production 변경 전 각각 expected 5 / actual 3으로 실패했다.
  - `ReceiveWindowSize` 한 줄 변경 후 강화 test 2/2, UDP 17/17, 전체 RIO 56/56, solution 520/520이 통과했다.
  - Release build는 경고 0/오류 0이었다.
  - load/open-loop 각 3회가 모두 3000/3000, drop/payload error/pool rented 0으로 hard pass했다.
  - latency warning 2개는 report-only로 유지했고 raw artifact는 repository baseline으로 채택하지 않았다.
- [x] 2026-07-13 RIO UDP depth 4 hardening implementation plan을 작성했다.
  - 정확한 test replacement, Red/Green 명령, 반복 gate raw 검증, 성공/실패 분기를 포함했다.
  - 반복 gate 전에는 구현 commit을 만들지 않고 결과 하나만 commit하도록 D013 경계를 유지했다.
  - live RIO UDP smoke 8/8, focused 18/18, full RIO 57/57 기준선을 다시 확인했다.
- [x] 2026-07-13 RIO UDP depth 4 hardening written spec 사용자 검토를 승인으로 닫았다.
- [x] 2026-07-11 RIO UDP 반복 안정성 hardening 방향을 fixed depth 4 written spec으로 정리했다.
  - 기존 slot owner와 request-context mapping을 재사용하고 production 변경을 내부 상수 1개로 제한했다.
  - receive registration reuse와 configurable/adaptive depth는 소유권 충돌과 과도한 범위 때문에 제외했다.
  - Red, close/drain, 반복 gate, 실패 시 rollback/diagnostics 전환 조건을 handoff-ready하게 명시했다.
- [x] 2026-07-11 현재 checkout의 explicit RIO TCP/UDP gate를 protocol별 3회 반복하고 실패 범위를 조사했다.
  - TCP load/open-loop 6개 report는 모두 3000/3000, drop/payload error/pool rented 0으로 hard pass했다.
  - UDP load 3회는 3000/3000이지만 open-loop 3회는 2996/2997/2999 수신으로 hard fail했다.
  - UDP open-loop send queue HWM은 2, transport drop과 payload error는 0이었다.
  - 같은 환경의 SAEA UDP open-loop는 3000/3000, 99.9 Hz로 통과했다.
  - RIO UDP focused tests 18/18은 통과해 지속 open-loop 부하에 대한 회귀 테스트 공백을 확인했다.
  - raw report와 summary는 임시 경로에만 두고 repository baseline으로 채택하지 않았다.
- [x] 2026-07-11 UDP pending-send HWM summary 수정 review stop을 사용자 진행 승인으로 닫았다.
- [x] 2026-07-11 UDP pending-send HWM summary/warning 누락을 TDD로 수정했다.
  - Red 1: UDP HWM 1/3 입력에서 summary min expected 1, actual 0으로 실패했다.
  - Red 2: UDP HWM 8 입력에서 기존 HWM warning collection이 비어 실패했다.
  - Green: summary와 warning이 TCP/UDP HWM의 max를 사용하며 legacy field/code/metric은 유지한다.
  - focused 2/2, Benchmark 118/118, solution 521/521, build 경고 0/오류 0이다.
  - 기존 SAEA UDP raw report CLI 재요약에서 load 1/1, open-loop 3/3을 확인했다.
  - 독립 리뷰는 Critical/Important/Minor finding이 없었다.
- [x] 2026-07-11 explicit RIO gate review stop을 사용자 진행 승인으로 닫았다.
- [x] 2026-07-10 현재 checkout explicit RIO TCP/UDP 4096B x 100Hz gate를 실행했다.
  - TCP load/open-loop: 99.8/100.0 Hz, p99 874.1/1024.8 us, HWM 1/2.
  - UDP load/open-loop: 99.9/100.0 Hz, p99 818.5/1229.7 us, UDP HWM 1/2.
  - TCP/UDP smoke와 baseline 모두 sent/received 일치, drop 0, payload error 0, pool rented 0이다.
  - sandbox package root 문제를 사용자 NuGet cache restore로 복구하고 Release build 경고 0/오류 0을 확인했다.
  - raw artifact는 임시 경로에만 두고 repository baseline으로 채택하지 않았다.
- [x] 2026-07-10 현재 checkout Release SAEA TCP/UDP 4096B x 100Hz gate를 실행했다.
  - TCP load/open-loop: 99.9/100.0 Hz, p99 455.0/675.1 us, HWM 1/2.
  - UDP load/open-loop: 99.8/100.0 Hz, p99 734.8/1023.6 us, UDP HWM 1/3.
  - 모든 run에서 sent/received 일치, drop 0, payload error 0, pool rented 0이다.
  - TCP는 explicit runner identity 재측정 후 repository reference envelope signal 0을 확인했다.
  - raw artifact는 임시 경로에만 두고 repository baseline으로 채택하지 않았다.
- [x] 2026-07-10 D239 benchmark 실행/reporting 책임 경계를 설계했다.
  - 48개 파일, reporting 계열 32개, runtime/BenchmarkDotNet 직접 의존 5개, workflow reporting 호출 9개를 대조했다.
  - raw report JSON 경계가 이미 존재하고 외부 소비자가 없어 현재 물리 분리는 과엔지니어링으로 판단했다.
  - 독립 소비자, 의존성 충돌, 반복 회귀, workflow 비용 중 실제 trigger가 생길 때만 다시 설계한다.
  - 사용자 승인으로 implementation plan 없이 review stop을 닫았다.
- [x] 2026-07-10 D238 구현 review stop을 사용자 진행 승인으로 닫았다.
- [x] 2026-07-10 D238 subscription readiness seam을 구현했다.
  - public shape와 입력/timeout/cancellation assertion Red를 확인하고 최소 10ms polling 계약으로 Green을 만들었다.
  - Dashboard/Benchmark TCP/UDP 네 호출부의 private reflection/polling을 제거했다.
  - Benchmark의 사용되지 않는 `Hps.Broker` project reference를 제거했다.
  - 독립 리뷰의 deadline 초과 성공 finding을 회귀 Red로 재현해 수정하고 대기 중 취소/음수 timeout 계약도 보강했다.
  - solution build 경고 0/오류 0, solution tests 519/519이다.
- [x] 2026-07-10 D238 subscription readiness seam 방향과 구현 경계를 설계했다.
  - wire ACK는 UDP reliability 범위를 열고 behavior probe는 측정을 오염시켜 제외했다.
  - 새 type/event/snapshot 없이 `BrokerServer` public wait method 하나로 수렴했다.
- [x] 2026-07-10 Sample Broker selector를 public `Select` 하나로 단순화했다.
  - 구조 테스트 Red가 기존 4/5/7-argument overload 3개를 검출했다.
  - 4/5-argument overload, 전용 fallback helper, legacy overload test를 제거했다.
  - selector tests 13/13, Sample Broker tests 25/25, solution build 경고 0/오류 0, solution tests 510/510이다.
- [x] 2026-07-10 루트 상태 문서를 현재 진입점 중심으로 압축하고 전체 원문 스냅샷을 보존했다.
- [x] D236 explicit sample io_uring 원격 Linux gate를 완료했다.
- [x] D235 sample broker explicit `--transport iouring` local implementation gate를 완료했다.

## Archive

- 압축 전 current/deferred/completed 전체 원문: `docs/agent-state/snapshots/2026-07-10-pre-compaction/TODOS.md`
- 2026-06-18 이전 완료 이력: `docs/agent-state/backlog/completed-history-2026-06-18.md`
