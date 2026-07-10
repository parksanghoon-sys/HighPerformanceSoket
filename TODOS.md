# TODOS.md

## Current TODOs

- [ ] `P1_NOW` D238 구현 review stop을 진행한다.
  - 설계: `docs/superpowers/specs/2026-07-10-subscription-readiness-seam-design.md`.
  - 구현: public wait method 하나, 네 reflection helper 제거, Benchmark→Broker 직접 reference 제거.
  - 검증: Server 37/37, Dashboard 13/13, Benchmark 116/116, solution 519/519, build 경고 0/오류 0.
  - 다음 단계: 사용자 또는 Claude 검토 전에는 다음 benchmark 구조 단위를 시작하지 않는다.

## Deferred Backlog

- [ ] `P2_LATER` benchmark 실행과 artifact/history 분석 책임을 분리한다.
  - 남은 일: `tests/Hps.Benchmarks`가 부하 실행, CLI parsing, baseline summary/history/envelope 생성까지 함께 소유한다.
  - 이유: 현재 workflow가 기능을 사용하고 있어 즉시 삭제할 수 없으며, 이번 상태 문서 정리와 독립된 구조 변경이다.
  - 목적: transport 성능 측정과 보고서 가공의 변경 이유 및 의존성을 분리한다.
  - 범위: `tests/Hps.Benchmarks`, `tests/Hps.Benchmarks.Tests`, benchmark workflows와 baseline docs.
  - 다음 단계: 기존 command/파일 계약을 목록화하고 project 분리와 폴더/entry point 분리의 비용을 비교한다.

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
