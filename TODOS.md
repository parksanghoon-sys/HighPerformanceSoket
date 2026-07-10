# TODOS.md

## Current TODOs

- [ ] `P1_NOW` 현재 checkout explicit RIO TCP/UDP gate 결과를 검토한다.
  - profile: Release, RIO, IPv4 loopback, 4096 bytes, 100 Hz, 30초, closed/open-loop 각 1회.
  - 결과: TCP/UDP smoke와 baseline 모두 hard pass, warning 0, drop/payload error/pool rented 0.
  - 제한: RIO repository reference가 없어 성능 우위/default 승격 근거가 아닌 현재 checkout gate다.
  - 다음 단계: 사용자 검토 뒤 UDP HWM summary 결함을 별도 TDD 단위로 진행한다.

## Deferred Backlog

- [ ] `P1_SOON` UDP pending-send HWM을 baseline summary/history/envelope에 반영한다.
  - 남은 일: raw UDP report에는 `udp-pending-send-queue-high-watermark`가 있으나 `BaselineSummaryGenerator`는
    `TcpPendingSendQueueHighWatermark`만 집계해 UDP summary의 legacy `tcp-hwm-*` 값과 warning이 0이 된다.
  - 이유: RIO gate 중 발견했지만 production/test 수정은 현재 measurement 단위와 분리해야 한다.
  - 목적: active protocol의 send queue HWM이 summary/history/envelope와 soft warning에 보존되게 한다.
  - 범위: `BaselineSummaryGenerator.cs`, 관련 summary tests, 필요 시 writer/envelope tests와 상태 문서.
  - 현재 상태: fresh raw 기준 SAEA UDP HWM은 load/open-loop 1/3, RIO UDP HWM은 1/2지만 summary는 0/0이다.
  - 호환성: JSON `tcp-hwm-*` field와 기존 warning code는 유지하고 TCP/UDP HWM의 max를 집계값으로 쓰는 최소안을 우선 검토한다.
  - 다음 단계: UDP HWM만 존재하는 report가 기존 summary HWM/warning에 반영되지 않는 assertion Red부터 작성한다.

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
