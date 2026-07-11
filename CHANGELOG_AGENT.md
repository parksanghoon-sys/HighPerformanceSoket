# CHANGELOG_AGENT.md

## Recent Work

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
