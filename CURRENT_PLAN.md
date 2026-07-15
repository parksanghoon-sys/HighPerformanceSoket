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
- RIO UDP receive window는 fixed depth 4로 보강됐고 4096B x 100 Hz load/open-loop 3회 delivery gate를 통과했다.
- 2026-07-14 current-head io_uring Linux contract run `29305055740`에서 project build, TRX 88/88과
  registered payload native fixed-send evidence를 다시 확인했다.

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
- depth 4 burst/close assertion Red를 재현한 뒤 `ReceiveWindowSize`만 2→4로 변경했다.
- depth 4 RIO UDP load/open-loop 각 3회가 모두 3000/3000으로 통과해 D240 가설을 수락했다.
- RIO UDP depth 4 구현 review stop은 2026-07-14 사용자 진행 승인으로 닫았다.
- `d63f3ba8147df4534268f851379dc05a3cb59427` push를 확인하고 같은 SHA로 explicit io_uring Linux gate를 갱신했다.
- run `29305055740`은 모든 step이 성공했고 artifact의 TRX와 native evidence도 수락 조건을 충족했다.
- current-head io_uring 원격 gate 결과 review stop은 2026-07-14 사용자 진행 승인으로 닫았다.
- D241 transport 등록-pump 시작 원자성 보강 review stop은 2026-07-15 사용자 진행 승인으로 닫았다.
- 2026-06-26 RIO UDP receive loop 검토를 현재 코드에 다시 대조했다.
- 당시 M2의 handler 예외·pool leak 검증과 M3의 close/drain 경계는 현재 bounded window tests로 닫혀 있다.
- 당시 M1의 per-datagram payload registration은 D113 소유권 결정에 따라 의도적으로 유지되며,
  L2의 `SOCKADDR_INET` 변환 임시 배열만 계약 변경 없이 줄일 수 있는 독립 후보로 남았다.

## 다음 단일 작업 단위

### D242 RIO UDP SOCKADDR 임시 할당 최소화 방향 검토

- 현재 `DecodeSockaddrInet`은 datagram마다 `byte[4]`, `IPAddress`, `IPEndPoint`를 만든다.
- net9.0의 `IPAddress(ReadOnlySpan<byte>)`를 사용하면 `byte[4]`만 제거할 수 있다.
- `EncodeSockaddrInet`의 `GetAddressBytes()`도 `TryWriteBytes(Span<byte>, out int)`로 임시 배열을 제거할 수 있다.
- `IPAddress`와 `IPEndPoint` 객체는 public `EndPoint` handler/send 계약과 Broker runtime target 모델 때문에
  이 국소 단위에서 제거하지 않는다.
- remote endpoint cache는 peer cardinality와 eviction/lifetime 정책을 새로 만들므로 현재 100 Hz 목표에는 과도하다.
- receive payload registration reuse는 D113의 receive/send registration 중첩 금지와 충돌하므로 D242에 섞지 않는다.
- 다음 단계는 allocation assertion Red가 환경에 안정적인지 확인한 뒤, 두 변환 helper만 최소 수정하는 설계를 확정하는 것이다.

## 최신 검증 기준선

- D235 local gate: solution build 경고 0/오류 0, solution tests 510/510, Sample Broker tests 25/25.
- D236 remote gate: io_uring TRX total/executed/passed 88, 실패/오류/timeout 0.
- native evidence: capability `Available`, registered payload registration과 TCP send loopback 통과,
  `registered payload fixed send path: hit` 확인.
- current-head remote gate: run `29305055740`, head SHA `d63f3ba8147df4534268f851379dc05a3cb59427`,
  Linux job과 io_uring/sample broker restore/build step 모두 성공.
- current-head artifact: TRX total/executed/passed 88, failed/error/timeout 0,
  capability `Available`, `registered payload fixed send path: hit` 확인.
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
- depth 2 repeated RIO UDP open-loop는 received 2996/2997/2999로 hard failure 3이었고 depth 4 Red의 원인이 됐다.
- SAEA UDP open-loop 대조: 3000/3000, 99.9 Hz, p99 673.3 us, HWM 3, hard pass다.
- depth 4 TDD/회귀: 강화 test 2/2, RIO UDP 17/17, 전체 RIO 56/56, solution 520/520,
  Release build 경고 0/오류 0이다.
- depth 4 repeated RIO UDP load: 3회 모두 3000/3000, actual 99.8~100.0 Hz,
  p50 175.0~180.4 us, p99 1039.0~1424.9 us, HWM 1이다.
- depth 4 repeated RIO UDP open-loop: 3회 모두 3000/3000, actual 99.9~100.0 Hz,
  p50 176.2~192.4 us, p99 1454.2~2131.8 us, HWM 2~4, hard failure 0이다.
- p99 warning 2개는 report-only이며 delivery 수락 조건과 분리했다.
- D241 TDD Red: TCP/UDP start 중 Stop은 expected false/actual true로 조기 완료했고,
  RIO/io_uring 종료 후 registration은 expected exception이 발생하지 않았으며,
  Dispose stop failure 뒤 Start는 expected `ObjectDisposedException`이 발생하지 않았다.
- D241 focused Green: Server 40/40, RIO 57/57, io_uring 89/89, SAEA Transport 44/44.
- D241 full gate: solution tests 525/525, Release build 경고 0/오류 0.
- D241 SAEA TCP load/open-loop: 3000/3000, actual 99.6/99.9 Hz, p50 179.9/182.6 us,
  p99 907.5/861.5 us, HWM 1/2, drop/payload error/pool rented 0.
- D241 SAEA UDP load/open-loop: 3000/3000, actual 99.9/99.9 Hz, p50 157.3/151.3 us,
  p99 1080.5/978.1 us, UDP HWM 1/6, drop/payload error/pool rented 0.
- D241 review follow-up Red: SAEA/RIO/io_uring 모두 pump-start 인자를 포함한 원자적 registration seam이 없어
  `Assert.NotNull` expected non-null/actual null로 실패했다.
- D241 review follow-up Green: 신규 registration 경합 3/3, SAEA Transport 45/45, RIO 58/58,
  io_uring 90/90, solution 528/528, Release build 경고 0/오류 0이다.
- D241 follow-up SAEA TCP load/open-loop: 3000/3000, actual 99.8/99.9 Hz, p50 163.4/196.6 us,
  p99 833.8/892.6 us, HWM 1/4, drop/payload error/pool rented 0.
- D241 follow-up SAEA UDP load/open-loop: 3000/3000, actual 99.9/99.9 Hz, p50 150.6/151.6 us,
  p99 907.0/839.4 us, UDP HWM 1/4, drop/payload error/pool rented 0.
- D241 follow-up RIO TCP load/open-loop: 3000/3000, actual 99.8/99.9 Hz, p50 185.4/190.8 us,
  p99 2070.5/1445.7 us, HWM 1/2, drop/payload error/pool rented 0.
- D241 follow-up RIO UDP load/open-loop: 3000/3000, actual 99.8/99.8 Hz, p50 168.8/173.5 us,
  p99 1779.6/1384.8 us, UDP HWM 1/2, drop/payload error/pool rented 0.

## 다음 후보

1. D242에서 RIO UDP SOCKADDR 변환의 임시 배열 제거 범위와 allocation Red를 확정한다.
2. 사용자가 push할 때 D241 설계/계획/구현, review follow-up과 review-stop 기록을 원격에 반영한다.
3. push 뒤 현재 HEAD로 explicit io_uring Linux 성능 gate를 별도 단위에서 갱신한다.

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
