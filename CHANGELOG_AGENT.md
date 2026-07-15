# CHANGELOG_AGENT.md

## Recent Work

### 2026-07-15 - transport lifecycle 경합 hardening 구현

- server TCP/UDP start-stop Red는 listen/bind release 전 Stop이 expected false/actual true로 완료돼 각각 실패했다.
- RIO/io_uring 종료 후 registration Red는 expected `TargetInvocationException`이 발생하지 않아 각각 실패했다.
- Dispose stop-failure Red는 이후 Start가 expected `ObjectDisposedException`을 내지 않아 실패했다.
- `BrokerServer` start/stop을 lifecycle gate로 직렬화하고 Dispose 종료 표식을 Stop보다 먼저 게시했다.
- RIO/io_uring `Register*`에 locked stopped guard를 적용하고 RIO completion port 전환과 UDP 실패 cleanup을 보강했다.
- focused tests는 Server 40/40, RIO 57/57, io_uring 89/89, SAEA Transport 44/44가 통과했다.
- solution tests 525/525, Release build 경고 0/오류 0이다.
- SAEA TCP/UDP load/open-loop 네 run은 모두 4096B x 100Hz에서 3000/3000, drop/payload error/pool rented 0이다.
- TCP p50/p99은 load 179.9/907.5 us, open-loop 182.6/861.5 us이며 HWM은 1/2다.
- UDP p50/p99은 load 157.3/1080.5 us, open-loop 151.3/978.1 us이며 UDP HWM은 1/6이다.
- public API, data hot path, backend 선택 정책은 변경하지 않았고 구현 사용자 review stop을 다음 진입점으로 남겼다.

### 2026-07-15 - transport lifecycle 경합 hardening 구현 계획

- 사용자 진행 승인으로 D241 written spec 검토를 닫고 설계 상태를 Accepted로 전환했다.
- server TCP/UDP start-stop 경합 Red 2개와 RIO/io_uring 종료 후 registration Red 2개의 exact test seam과 명령을 확정했다.
- Green은 `BrokerServer` control-path lifecycle gate, native locked stopped guard, RIO completion port 잠금 전환과 UDP 실패 cleanup으로 제한했다.
- 세부 계획 검토에서 Dispose가 Stop 뒤 `_disposed`를 기록하는 좁은 Start 경합을 확인해, 종료 표식을 먼저 게시하도록 D241을 보완했다.
- 계층별 focused tests, solution build/tests, Windows SAEA TCP/UDP 4096B x 100Hz target gate와 실패 시 scope 확대 금지를 계획에 명시했다.
- production code와 tests는 변경하지 않았으며 implementation-plan 사용자 검토를 다음 진입점으로 남겼다.
- 설계 commit `f814cc1`과 이번 plan/state commit의 push는 사용자가 별도 수행한다.

### 2026-07-15 - transport lifecycle 경합 hardening 설계

- 현재 구현 검토에서 `BrokerServer`의 비동기 start resource 게시와 Stop 경합, RIO/io_uring의 종료 후 `Register*` 허용을 확인했다.
- server-only와 native-only 수정은 각각 직접 transport consumer 또는 server resource 게시 race를 남겨 제외했다.
- D241로 server lifecycle operation 직렬화와 native locked stopped guard를 함께 적용하는 최소 설계를 채택했다.
- written spec에 deterministic Red 4개, ownership cleanup, focused/full gate와 범위 밖 항목을 명시했다.
- `PLAN.md`의 오래된 Phase 1 snapshot과 완료된 push blocker를 현재 `master` 상태에 맞게 정리했다.
- production code와 tests는 변경하지 않았으며 written-spec 사용자 검토를 다음 진입점으로 남겼다.
- 이번 written spec/state commit의 원격 반영은 사용자 검토 뒤 `P1_SOON`으로 수행한다.

### 2026-07-14 - current-head io_uring 원격 gate review stop 종료

- 사용자의 다음 진행 승인으로 run `29305055740`의 build/TRX/native evidence 검토를 완료 처리했다.
- 현재 목표와 열린 요구를 재평가한 결과 즉시 실행 가능한 production code 작업은 없다.
- RIO IPv6, server diagnostics와 workflow allow-list test는 각각의 기존 trigger가 없어 deferred 상태를 유지했다.
- 원격 gate 기록과 review stop 종료 문서 커밋은 로컬에 완료했다.
- direct `git push`는 현재 실행 정책에서 차단돼 사용자 push를 `P1_SOON`으로 남겼다.
- production code와 tests는 변경하지 않았다.

### 2026-07-14 - current-head io_uring 원격 Linux gate 갱신

- local HEAD와 `origin/master`가 `d63f3ba8147df4534268f851379dc05a3cb59427`에서 일치함을 확인했다.
- explicit `io_uring Linux Contract` run `29305055740`을 `master`에 실행했고 모든 workflow step이 성공했다.
- io_uring tests와 sample broker의 project-scoped restore/build가 성공했다.
- artifact TRX는 total/executed/passed 88, failed/error/timeout 0이었다.
- TRX에서 capability `Available`과 `registered payload fixed send path: hit`를 직접 확인했다.
- production code와 tests는 변경하지 않았고 원격 artifact는 임시 경로에서만 검증했다.

### 2026-07-14 - RIO UDP depth 4 구현 review stop 종료

- 사용자의 다음 진행 승인으로 depth 4 구현 결과 review stop을 닫았다.
- 현재 목표와 열린 요구를 재평가한 결과 즉시 실행 가능한 로컬 코드 작업은 없다.
- push와 explicit io_uring 원격 Linux gate 갱신은 사용자의 push 가능 시점까지 `P1_SOON`으로 defer했다.
- RIO IPv6, server diagnostics와 workflow allow-list test는 각각 제품 요구 또는 실제 회귀 trigger가 없어 승격하지 않았다.
- production code와 tests는 변경하지 않았다.

### 2026-07-14 - RIO UDP fixed depth 4 반복 안정성 보강

- RIO UDP smoke 8/8과 기존 focused tests 18/18로 RIO availability와 기준선을 먼저 확인했다.
- blocked-handler burst와 close-owner test가 production 변경 전 각각 expected 5 / actual 3 assertion Red를 재현했다.
- production 변경은 `RioUdpEndpoint.ReceiveWindowSize` 2→4 한 줄이며 기존 slot owner와 직렬 dispatch를 재사용했다.
- 강화 test 2/2, UDP 17/17, 전체 RIO 56/56, solution 520/520이 통과했고 Release build는 경고 0/오류 0이었다.
- RIO UDP load/open-loop 각 3회가 모두 sent/received 3000/3000, drop/payload error/pool rented 0으로 hard pass했다.
- load actual rate는 99.8~100.0 Hz, p99은 1039.0~1424.9 us, HWM은 1이었다.
- open-loop actual rate는 99.9~100.0 Hz, p99은 1454.2~2131.8 us, HWM은 2~4였다.
- p99 warning 2개는 report-only로 유지했고 raw artifact는 임시 경로에서만 검증했다.

### 2026-07-13 - RIO UDP depth 4 hardening 구현 계획

- 승인된 written spec을 Red 2개, 최소 Green, test/build, UDP runs 3, 단일 결과 commit 순서로 구체화했다.
- gate 전 중간 commit을 금지해 depth 4가 반복 delivery를 통과한 경우에만 production fix를 수락한다.
- gate 실패 시 task-owned code/test만 복원하고 depth 8 확대 없이 diagnostics 설계로 돌아가는 분기를 명시했다.
- exact test code, raw report 구조 검증, stage allow-list와 no-push review stop을 포함했다.
- 현재 live 기준선은 RIO UDP smoke 8/8, focused UDP tests 18/18, full RIO tests 57/57이다.
- production code와 tests는 변경하지 않았다.

### 2026-07-11 - RIO UDP depth 4 반복 안정성 hardening 설계

- D240 이후 다음 변경을 public 설정 없는 internal fixed receive depth 4 검증으로 제한했다.
- 기존 slot owner, request-context mapping, 직렬 handler dispatch를 재사용해 production 변경은 우선 상수 1개만 허용한다.
- blocked handler burst와 close 중 `ReceivePool.RentedCount` 5→0을 Red/Green 계약으로 정했다.
- 구현 수락은 UDP load/open-loop 각 3회 3000/3000과 drop/payload error/pool rented 0으로 제한했다.
- gate 실패 시 depth 8로 확대하지 않고 변경을 되돌린 뒤 누락 위치 diagnostics 설계로 돌아가도록 했다.
- receive registration reuse, adaptive depth, default promotion, 새 diagnostics API는 범위 밖이다.

### 2026-07-11 - RIO TCP/UDP 3회 반복 안정성 gate

- 현재 checkout에서 RIO TCP/UDP 4096B x 100 Hz x 30초 load/open-loop를 protocol별 3회 반복했다.
- TCP 6개 report와 UDP load 3개 report는 sent/received 3000/3000, drop/payload error/pool rented 0으로 hard pass했다.
- UDP open-loop는 received 2996/2997/2999로 3회 모두 hard fail했고 send queue HWM은 2였다.
- 같은 환경의 SAEA UDP open-loop는 3000/3000, 99.9 Hz로 통과해 RIO receive-side로 조사 범위를 좁혔다.
- 기존 RIO UDP focused tests 18/18은 통과해 depth 2 계약 테스트와 지속 open-loop 부하 사이의 검증 공백을 확인했다.
- D240으로 D118의 단발 통과를 반복 안정성 근거로 확대하지 않고, 다음 단위를 bounded window hardening 설계로 제한했다.
- raw artifact는 임시 경로에만 두었고 production code와 tests는 변경하지 않았다.

### 2026-07-11 - UDP pending-send HWM summary 수정

- `BaselineSummaryGenerator`가 UDP HWM을 무시해 summary/history/envelope와 warning에서 0으로 보이던 결함을 수정했다.
- summary min/max Red는 expected 1/actual 0, warning Red는 empty collection으로 실패함을 확인했다.
- TCP/UDP pending-send HWM의 max를 공통 집계값으로 사용하되 JSON `tcp-hwm-*`와 기존 warning code/metric은 유지했다.
- focused 2/2, Benchmark 118/118, solution 521/521, build 경고 0/오류 0이다.
- 기존 SAEA UDP raw report CLI 재요약에서 load HWM 1/1, open-loop HWM 3/3을 확인했다.
- 독립 리뷰는 finding이 없었고 mixed positive HWM 입력의 residual risk는 `Math.Max`의 명시성 때문에 낮다고 판단했다.

### 2026-07-10 - 현재 checkout explicit RIO TCP/UDP gate

- Release RIO TCP/UDP smoke와 4096 bytes x 100 Hz x 30초 closed/open-loop를 임시 경로에서 실행했다.
- TCP load/open-loop는 99.8/100.0 Hz, p99 874.1/1024.8 us, HWM 1/2다.
- UDP load/open-loop는 99.9/100.0 Hz, p99 818.5/1229.7 us, UDP HWM 1/2다.
- 모든 run이 hard pass, warning 0, drop/payload error/pool rented 0이다.
- sandbox restore가 package root를 `CodexSandboxOffline`로 기록해 후속 build가 실패한 원인을 확인했다.
- `NUGET_PACKAGES=C:\Users\ADMIN\.nuget\packages`, `NuGetAudit=false`로 restore 후 Release build 경고 0/오류 0과 smoke를 재확인했다.
- UDP raw HWM을 summary가 무시하는 reporting 결함은 별도 `P1_SOON` TDD 단위로 분리했다.
- RIO reference가 없어 SAEA 대비 우위/default 승격은 주장하지 않고 raw artifact도 repository에 채택하지 않았다.

### 2026-07-10 - 현재 checkout Release SAEA TCP/UDP gate

- 임시 디렉터리에서 4096 bytes x 100 Hz x 30초 closed/open-loop를 TCP/UDP 각 1회 실행했다.
- TCP load/open-loop는 99.9/100.0 Hz, p99 455.0/675.1 us, HWM 1/2다.
- UDP load/open-loop는 99.8/100.0 Hz, p99 734.8/1023.6 us, UDP HWM 1/3이다.
- 모든 run이 hard pass, warning 0, drop/payload error/pool rented 0이다.
- 첫 TCP run은 runner-id 미지정으로 reference와 비교 불가였고 raw를 수정하지 않은 채 identity를 지정해 재측정했다.
- 재측정 TCP는 reference summary 9개와 envelope-compatible true, signal 0이다.
- 임시 raw artifact는 repository baseline으로 자동 채택하지 않았다. production code와 tests는 변경하지 않았다.

### 2026-07-10 - D239 Benchmark 실행/reporting 경계 설계

- `Hps.Benchmarks` 48개 파일과 tests/workflow/문서 소비를 실제 코드 기준으로 분류했다.
- reporting 계열은 32개지만 production 외부 소비자는 없고 workflow는 단일 executable을 안정적으로 사용한다.
- 별도 library는 연결만 늘리고 별도 executable은 parser/test/workflow 이관 비용이 커 현재는 채택하지 않았다.
- raw report JSON을 실행과 reporting의 논리 경계로 고정하고 물리 분리 trigger 네 가지를 명시했다.
- 문서 전용 단위이며 production code, tests, workflow, 기존 `.claude/review` 파일은 변경하지 않았다.

### 2026-07-10 - D238 subscription readiness seam 구현

- `BrokerServer.WaitForSubscriberCountAsync` 하나로 transient aggregate count readiness를 제공한다.
- public shape Red 1건과 입력/timeout/cancellation behavior Red 6건을 확인한 뒤 최소 10ms polling으로 Green을 만들었다.
- 독립 리뷰의 deadline 초과 성공 finding을 별도 Red로 재현하고 남은 시간 대기와 deadline 우선 판정으로 수정했다.
- Dashboard와 Benchmark TCP/UDP 네 reflection helper를 제거하고 Benchmark의 Broker 직접 project reference를 제거했다.
- Server 37/37, Dashboard 13/13, Benchmark 116/116, solution 519/519를 통과했고 build는 경고 0/오류 0이다.
- wire ACK, event/snapshot, endpoint별 readiness와 publish hot path는 변경하지 않았다.

### 2026-07-10 - Sample Broker selector surface 단순화

- 구조 테스트 Red에서 public `Select` 3개를 검출한 뒤 사용되지 않는 4/5-argument overload를 제거했다.
- production selector는 실제 Program이 사용하는 7-argument entry 하나만 남겼다.
- 테스트 helper는 reflection invocation과 legacy overload 대신 production entry를 직접 호출한다.
- selector tests 13/13, Sample Broker tests 25/25, solution build 경고 0/오류 0, solution tests 510/510이다.
- 독립 리뷰의 문서 정합성 finding을 반영해 D233/D234 compatibility 하위 결정을 superseded 처리하고 stale test comment를 갱신했다.

### 2026-07-10 - 상태 문서 압축 및 실행 우선순위 정리

- 압축 전 루트 상태 문서 4개를 SHA-256과 함께 `docs/agent-state/snapshots/2026-07-10-pre-compaction/`에 보존했다.
- 루트 상태 문서를 12,626줄에서 250줄 이하로 줄이고 현재 목표, 실행 항목, 활성 결정, 최근 이력만 남겼다.
- 기존 7월 changelog에 빠져 있던 D204~D236 상세 이력을 월별 archive로 이동했다.
- D237 legacy overload test는 현재 결함이 아니므로 보류하고 selector surface 단순화를 다음 단위로 정했다.
- 문서 경로 존재, snapshot hash, archive heading, `git diff --check`를 검증했다. 코드와 테스트는 변경하지 않았다.

### 2026-07-10 - D236 explicit sample io_uring remote Linux gate

- Linux workflow에서 io_uring tests와 sample broker restore/build가 성공했다.
- TRX 88/88 통과와 `registered payload fixed send path: hit`를 확인했다.
- sample process 장기 실행, default 승격, zero-copy, 성능 우위 증거로는 확대하지 않는다.

### 2026-07-10 - D235 explicit sample io_uring local gate

- parser, selector, Program, workflow를 독립 커밋으로 구현했다.
- solution build 경고 0/오류 0, solution tests 510/510, Windows explicit mode fail-closed를 확인했다.

### 2026-07-10 - D232 Interface Server usage guide 검증

- 실제 sample loopback과 public API를 기준으로 io_uring direct opt-in 사용법을 갱신했다.
- registered payload path와 default/zero-copy 비주장 경계를 문서화했다.

### 2026-07-10 - D231 registered payload remote Linux gate

- production TCP payload registered pool hit가 native `WRITE_FIXED`를 사용함을 원격 artifact로 확인했다.
- 이는 receive 1회 복사를 제거하지 않으므로 end-to-end zero-copy 증거가 아니다.

### 2026-07-10 - D230 registered payload local gate

- buffer source/owner, Protocol injection, io_uring pool, Server provider seam, fixed-send opt-in을 구현했다.
- solution build 경고 0/오류 0, solution tests 502개를 통과했다.

### 2026-07-09 - D229 registered payload implementation plan

- concrete backend 역의존 없이 transport payload source provider seam으로 연결하는 순서를 확정했다.

### 2026-07-09 - D227 Interface Server usage guide

- sample CLI와 direct embedding의 TCP/UDP wire contract, transport 선택, 진단 범위를 문서화했다.

### 2026-07-09 - D226 registered payload pool design

- production TCP fixed-write 연결 전에 queue-scoped registered payload owner/source 경계를 설계했다.

### 2026-07-09 - D224 fixed send registry remote gate

- connection-scoped fixed send registry lifetime의 Linux native contract를 확인했다.
- production payload/default 연결은 별도 근거가 필요하다고 유지했다.

## Archive

- 2026-07 상세 이력: `docs/agent-state/changelog/2026-07.md`
- 2026-06 상세 이력: `docs/agent-state/changelog/2026-06.md`
- 압축 전 전체 루트 원문: `docs/agent-state/snapshots/2026-07-10-pre-compaction/CHANGELOG_AGENT.md`
