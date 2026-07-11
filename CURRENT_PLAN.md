# CURRENT_PLAN.md - 현재 실행 지점

## 목표

- 고성능 TCP/UDP Interface Server에서 4096바이트 메시지를 100 Hz로 처리한다.
- 정확성, 누수 없음, bounded backpressure를 먼저 보장하고 지연과 처리량은 재현 가능한 측정으로 판단한다.
- 상위 Protocol/Broker는 OS별 transport 구현을 모르며, 기본 transport 의미는 명시적 근거 없이 바꾸지 않는다.

## 현재 상태

- Phase 1~5의 메모리, SAEA, Protocol/Broker/Server, benchmark, RIO 경로가 구현되어 있다.
- Phase 6 io_uring은 native queue, TCP/UDP pump, fixed buffer registration과 registered payload opt-in 경로까지 구현되어 있다.
- D231 원격 Linux gate에서 production TCP payload의 registered pool hit와 native `WRITE_FIXED` 사용을 확인했다.
- D236 원격 Linux gate에서 sample broker의 explicit `--transport iouring` project build와 backend native contract를 확인했다.
- 위 증거는 end-to-end zero-copy, `auto`/default 승격, latency hard gate를 뜻하지 않는다.
- `TransportFactory.CreateDefault()`는 SAEA 기본값을 유지하고 sample `auto`는 RIO preferred/SAEA fallback 의미를 유지한다.

## 최근 정리 결과

- 2026-07-10 상태 문서 압축 전 원문을 `docs/agent-state/snapshots/2026-07-10-pre-compaction/`에 보존했다.
- `CURRENT_PLAN.md`, `TODOS.md`, `CHANGELOG_AGENT.md`, `DECISIONS.md`는 현재 진입점만 남기도록 압축했다.
- Sample Broker selector의 사용되지 않는 4/5-argument overload와 전용 fallback helper를 제거했다.
- selector 정책 테스트는 실제 7-argument production entry를 직접 사용하며 public `Select`는 하나만 남았다.
- D237 legacy overload test 제안은 overload 제거로 종료됐다.
- D238로 cross-module subscription reflection을 단일 `BrokerServer.WaitForSubscriberCountAsync` seam으로 교체했다.
- Dashboard/Benchmark 네 호출부의 reflection/polling을 제거했고 Benchmark의 불필요한 Broker 직접 참조도 제거했다.
- D238 구현 review stop은 사용자 진행 승인으로 닫았다.
- D239에서 benchmark 실행/reporting 책임을 조사했고 현재는 project를 나누지 않는 것으로 결정했다.
- D239 written design은 사용자 승인으로 구현 계획 없이 닫았다.
- 현재 checkout의 Release SAEA TCP/UDP 4096B x 100Hz gate를 임시 artifact로 다시 실행해 hard pass를 확인했다.
- SAEA gate review stop은 사용자 진행 승인으로 닫았다.
- 현재 checkout의 explicit RIO TCP/UDP gate도 같은 profile로 실행해 hard pass를 확인했다.
- RIO gate review stop은 사용자 진행 승인으로 닫았다.
- UDP raw pending-send HWM을 legacy summary/history/envelope HWM과 warning에 반영하도록 수정했다.
- UDP HWM 수정 review stop은 사용자 진행 승인으로 닫았다.
- 현재 checkout의 explicit RIO TCP/UDP gate를 protocol별 3회 반복했다.
- TCP 6개 report와 UDP load 3개 report는 hard pass였지만 UDP open-loop 3개 report는 모두 delivery hard gate를 실패했다.
- 같은 환경의 SAEA UDP open-loop 대조는 3000/3000으로 통과해 RIO receive window 쪽으로 조사 범위를 좁혔다.

## 다음 단일 작업 단위

### RIO UDP depth 4 hardening written design review stop

- `docs/superpowers/specs/2026-07-11-rio-udp-repeat-stability-hardening-design.md`에 D240 보강 설계를 작성했다.
- public 설정이나 새 abstraction 없이 내부 fixed receive depth를 2에서 4로 검증한다.
- blocked handler 중 current 1개와 posted slot 4개, close 후 pool 0을 Red로 먼저 고정한다.
- 구현 수락 gate는 RIO UDP load/open-loop 각 3회 모두 3000/3000과 drop/payload error/pool rented 0이다.
- 한 번이라도 delivery hard fail이면 depth 8로 확대하지 않고 변경을 되돌린 뒤 누락 위치 diagnostics 설계로 돌아간다.
- 구현 계획과 production 변경은 written spec 사용자 검토 전까지 시작하지 않는다.

## 최신 검증 기준선

- D235 local gate: solution build 경고 0/오류 0, solution tests 510/510, Sample Broker tests 25/25.
- D236 remote gate: io_uring TRX total/executed/passed 88, 실패/오류/timeout 0.
- native evidence: capability `Available`, registered payload registration과 TCP send loopback 통과,
  `registered payload fixed send path: hit` 확인.
- selector 단순화: 구조 Red가 public `Select` 3개를 검출했고, Green 후 selector tests 13/13,
  Sample Broker tests 25/25, solution build 경고 0/오류 0, solution tests 510/510이다.
- D238 TDD: 최초 public/behavior Red에 더해 review에서 deadline 초과 성공 Red를 재현했고 focused API tests 9/9을 통과했다.
- D238 회귀: Server 37/37, Dashboard 13/13, Benchmark 116/116, solution tests 519/519 통과.
- D238 build: solution build 경고 0/오류 0, 네 cross-module reflection match 0, Benchmark Broker 직접 참조 0.
- D239 구조 확인: Benchmark 파일 48개 중 reporting 계열 32개, runtime/BenchmarkDotNet 직접 의존 5개,
  reporting workflow 호출 9개, 외부 production 소비자 0이다.
- fresh SAEA TCP: load/open-loop actual 99.9/100.0 Hz, p50 141.9/150.7 us, p99 455.0/675.1 us,
  send queue HWM 1/2, drop/payload error/pool rented 0, envelope signal 0.
- fresh SAEA UDP: load/open-loop actual 99.8/100.0 Hz, p50 128.7/152.2 us, p99 734.8/1023.6 us,
  UDP send queue HWM 1/3, drop/payload error/pool rented 0.
- fresh RIO TCP: load/open-loop actual 99.8/100.0 Hz, p50 156.4/165.7 us, p99 874.1/1024.8 us,
  send queue HWM 1/2, drop/payload error/pool rented 0.
- fresh RIO UDP: load/open-loop actual 99.9/100.0 Hz, p50 134.6/142.5 us, p99 818.5/1229.7 us,
  UDP send queue HWM 1/2, drop/payload error/pool rented 0.
- sandbox restore가 잘못된 package root를 기록해 후속 build가 실패했으나, 사용자 NuGet cache를 명시해
  restore한 뒤 Release build 경고 0/오류 0과 재빌드 binary TCP/UDP RIO smoke를 확인했다.
- UDP HWM TDD: summary min/max Red는 expected 1/actual 0, warning Red는 empty collection으로 실패했다.
- UDP HWM Green: focused 2/2, Benchmark 118/118, solution 521/521, build 경고 0/오류 0.
- CLI integration: 기존 SAEA UDP raw report 재요약이 load HWM 1/1, open-loop HWM 3/3을 출력했다.
- repeated RIO TCP: load actual 99.6~100.0 Hz, p99 median/max 1141.5/1409.6 us, HWM max 1;
  open-loop actual 99.9~100.0 Hz, p99 median/max 1301.9/1388.5 us, HWM max 4, hard failure 0이다.
- repeated RIO UDP load: 3회 모두 3000/3000, p99 median/max 1169.0/2776.2 us, HWM max 1이다.
- repeated RIO UDP open-loop: received 2996/2997/2999, actual 85.6~85.7 Hz, p99 median/max 1264.7/1715.9 us,
  HWM max 2, hard failure 3이다. 5초 receive timeout이 actual rate 계산에 포함됐다.
- SAEA UDP open-loop 대조: 3000/3000, 99.9 Hz, p99 673.3 us, HWM 3, hard pass다.
- RIO UDP focused tests 18/18, Benchmark Release build 경고 0/오류 0이다.

## 다음 후보

1. written spec 검토 승인 뒤 depth 4 hardening 구현 계획을 별도 문서 단위로 작성한다.
2. 구현은 Red 2개, 내부 상수 최소 변경, focused/full tests, UDP `--runs 3` gate 순서로만 진행한다.
3. push 가능 시 현재 local 커밋을 원격에 반영하고 explicit io_uring remote gate를 갱신한다.
4. RIO full IPv6와 server-level diagnostics는 실제 제품 요구가 열릴 때만 재평가한다.

## 이번 범위 밖

- native backend 내부 class의 일괄 병합
- default transport 승격
- end-to-end zero-copy 주장
- latency warning의 hard gate 전환
- benchmark report 기능 추가
- readiness seam을 wire ACK 또는 범용 diagnostics model로 확장
- 근거 없는 Benchmark/reporting project 분리

## Archive

- 압축 전 전체 상태: `docs/agent-state/snapshots/2026-07-10-pre-compaction/`
- 상세 변경 이력: `docs/agent-state/changelog/2026-07.md`
- 상세 결정 이력: `docs/agent-state/decisions/2026-07.md`
