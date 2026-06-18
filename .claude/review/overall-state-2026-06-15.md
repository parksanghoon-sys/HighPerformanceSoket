# overall-state-2026-06-15 — 설계 대비 구현 상태 재검토 (HEAD = 4c6f521)

검토자: Claude / 날짜: 2026-06-15
이전 종합 검토: `overall-state-2026-06-11.md` (HEAD 465268d). 이 문서는 그 이후 변경을 반영한 재검토다.

## 1. 검증

- `dotnet build HighPerformanceSocket.slnx` → 경고 0, 오류 0.
- `dotnet test HighPerformanceSocket.slnx` → 통과 106, 실패 0, 건너뜀 0.
  (Buffers 18 + Transport 37 + Protocol 28 + Broker 18 + Server 5)

## 2. 한 줄 판정

**2026-06-11 검토가 최우선으로 지목한 H1(백프레셔)·H2(연결종료 구독정리)·H3(end-to-end 결선)이 모두
해소됐다.** 이로써 PLAN Phase 1~3의 "동작하는 크로스플랫폼 TCP 브로커"가 완성됐고, 설계(DECISIONS
D005~D012)와 코드가 정합한다. 다음 게이트는 **Phase 4 벤치마크로 헤드라인 목표(4096B×100Hz, 지연
누적 없음)를 정량 검증**하는 것이다. 현재까지 코드 결함(버그)은 발견되지 않았다.

## 3. 이전 H-블로커 해소 확인

| 항목 | 상태 | 근거 |
|---|---|---|
| H1 백프레셔(D012) | ✅ 해소 | `TransportConnection.TryAcceptSend`/`SaeaUdpEndpoint`에 capacity 16 drop-oldest. evict ref 정확히 1회 Release, evict/dequeue/close 단일 lock 직렬화. drop counter + `ITransportDiagnostics` snapshot. |
| H2 연결종료 구독정리 | ✅ 해소 | `SubscriptionTable.UnsubscribeAll(IConnection)` + `BrokerTcpFrameHandler.OnConnectionClosed`. 정상 종료(recv 0/SocketException)는 `NotifyConnectionClosed → receiveHandler.OnConnectionClosed → UnsubscribeAll` 경로로 수렴. malformed 직접 close 경로도 `UnsubscribeAll` 선행. |
| H3 end-to-end 결선 | ✅ 해소 | `BrokerServer`(accept loop + wiring), `BrokerTcpFrameHandler`(decode→subscribe/publish/cleanup), 샘플 publisher/subscriber/broker-server. `BrokerServer + SaeaTransport` 실제 TCP 단일·다중 subscriber fan-out 통합 테스트 green, fan-out 후 `RentedCount==0`. |

## 4. 교차 강점 (유지됨)

소유권 규율(guard ref + 구독자 AddRef + 정확히-1회 Release)이 transport send / UDP datagram / 프레임
조립 / 브로커 fan-out 전 계층에서 일관되며 누수 0 테스트로 방어된다. BipBuffer(SPSC, M1/M2 fix),
RefCountedBuffer(부활 거부·이중반환 거부), Pool(RentedCount 누수 감지) 모두 견고. C# 8 제약과 한국어
주석 규칙도 준수. 핫패스 할당 회피(ArrayPool snapshot, ref struct view)도 일관적.

## 5. 미결 / 권고 (우선순위 순)

### P1 — Phase 4 벤치마크로 헤드라인 목표 정량화 (최우선)
- 프로젝트의 핵심 목표("4096B×100Hz를 지연 누적 없이")가 아직 **재현 가능한 성능 게이트로 고정되지
  않았다.** 기능은 완성됐으나 "목표 달성"은 미검증 상태다.
- 필요: p50/p99 지연, 허용 큐 적체, 측정 시간/동시연결/fan-out 배율, SAEA 기준선 수치. `tests/Hps.Benchmarks/` 신설.
- 이 게이트가 있어야 이후 SAEA pump 최적화·RIO/io_uring의 개선폭을 의미 있게 비교할 수 있다.

### P2 — 백프레셔 정책이 설계 기본값과 다름 (설계 정합성)
- PLAN/AGENTS는 "기본=느린 소비자 끊기, **옵션**=drop-oldest"로 명시하나, 구현은 **drop-oldest만**,
  그것도 capacity 16 하드코딩·비설정. drop은 counter 증가 외에 조용히 메시지를 버린다.
- 4096B×100Hz·정상 소비자 환경에서는 큐가 거의 비어 trigger되지 않아 당장 문제는 없다. 그러나
  "기본 정책 = 끊기"라는 설계 의도와 코드가 갈린다. **의도적 변경인지, PLAN을 코드에 맞춰 갱신할지,
  코드를 설계에 맞출지 한 번 결정**하는 것이 좋다(D012/Deferred Backlog와 함께).
- capacity 16 = 100Hz에서 약 160ms 버퍼링. 매직넘버이므로 벤치마크 시 목표 기반으로 도출/설정화 권고.

### P3 — UDP가 브로커에 미결선 (범위 결정 필요)
- Transport UDP datagram 기준선(bind/send/recv/echo)은 있으나, **UDP 명령 경로·브로커 fan-out은 없다.**
  현재 end-to-end pub/sub는 TCP 전용이다. AGENTS는 UDP `1 datagram=1 메시지` 프레이밍을 규칙으로 두지만
  CURRENT_PLAN은 UDP fan-out backpressure를 Deferred로 미룸.
- v1 범위에 UDP pub/sub를 포함할지 명시적으로 결정 권고. 포함이면 별도 작업 단위, 제외면 문서에 범위 고정.

### L1 — D008 topic entry 영구 누적 (낮음, 기존 동일)
- NoCleanup tradeoff로 distinct topic 키가 영구 누적. churn이 아닌 distinct-topic 수 비례라 우선순위 낮음.
  필요 시 주기적 안전 sweep 도입.

### L2 — 잔여 baseline (낮음, 의도된 후속)
- protocol error 응답 없음(malformed=close), SAEA SocketAsyncEventArgs pump/송신 BipBuffer 합치기,
  RIO/io_uring(Phase 5/6), configurable pending capacity, drop log/sampling. 모두 문서에 후속으로 기록됨.

### N1 — Phase 3 완료 기준의 "샘플 수동 fan-out 확인" 미실행 여부 확인
- 샘플 3종은 build green이나, PLAN Phase 3 완료 기준의 "샘플로 수동 팬아웃 확인"이 실제 실행됐는지는
  문서상 불명확(통합 테스트가 동일 경로를 자동 검증하므로 기능 위험은 낮음). 1회 수동 실행 기록 권고.

## 6. 결론

코드 품질·설계 정합성·테스트 방어 모두 양호하며 이전 검토의 must-fix/H-블로커는 잔여 없음. **다음은
새 기능 추가보다 Phase 4 벤치마크로 목표를 정량 게이트화하는 것**을 권한다. P2(백프레셔 정책)와
P3(UDP 범위)는 구현이 아니라 **설계 결정**이 필요한 항목이므로 사용자 확인 후 진행한다.

## 7. 추가 노트 — P1 착수 반영 (HEAD 4c6f521 → b6e14cd)

이 검토 작성 직후 확인하니, 위 §5 P1(Phase 4 벤치마크)이 이미 **착수**됐다. 빌드는 여전히 0경고/0오류.

- `6d6c1fd feat: add phase4 benchmark scaffold` (D050): `tests/Hps.Benchmarks/` 신설. 헤드라인 목표를
  `BenchmarkTargets` 코드 상수로 고정(4096B / 100Hz / 30초 / 3000 메시지 / subscriber 1 / topic `alpha`).
  게이트 정의 `sent==received && dropped==0 && pool-rented==0 && p50/p99 기록`. pinned pool microbench 골격 포함.
- `b6e14cd feat: add tcp loopback benchmark smoke`: 실제 `BrokerServer + SaeaTransport` 경로로 8개 메시지를
  왕복시켜 계측 경계(지연 측정·누수 0)만 확인하는 smoke runner. green.

**미완결**: smoke는 8개·rate pacing 없음. 실제 게이트인 **지속 100Hz×30초=3000 메시지 부하 runner와 리포트
기록은 미구현**이다. D050 본문도 "성능 달성을 주장하지 않는다"고 명시 → **목표 달성은 여전히 미검증**.
따라서 §5 P1 우선순위는 "착수됨, 미완결"로 갱신하며 권고는 동일하다(부하 runner로 게이트를 닫을 것).

**관찰(낮음)**: smoke runner가 `BrokerServer._subscriptions`를 reflection 으로 읽어 구독 등록을 기다린다.
와이어 프로토콜에 SUBSCRIBE ack 가 없어 publish-start race 를 white-box 로 우회하는 것이다. 실제 load runner 도
같은 시작 동기화 문제를 만나므로, runner 설계 때 동기화 방식을 함께 정하는 것이 좋다.

## 8. 추가 노트 — P1 완결·목표 실측 (HEAD b6e14cd → c5a9409)

`c5a9409 feat: add tcp loopback benchmark load`로 부하 runner와 pass/fail 게이트가 들어왔다.
smoke/load 를 `TcpLoopbackScenarioRunner` 하나로 통합하고, `Program`에 `--smoke`/`--load`/`--target` 진입점 추가.
게이트(`TcpLoopbackRunResult.Passed`): `sent==planned && sent==received && dropped==0 && pool-rented==0`.

**실측 (`--load`, Release, 이 검토에서 직접 실행)**:

```
load-result: pass
payload-bytes: 4096   target-rate-hz: 100   duration: 30s   planned: 3000
sent: 3000  received: 3000  dropped: 0  pool-rented: 0
actual-rate-hz: 99.8   p50-latency-us: 173.7   p99-latency-us: 768.4   elapsed-ms: 30062
```

→ **헤드라인 목표 "4096B×100Hz, 지연 누적 없음"이 SAEA loopback 기준선에서 정량 검증됐다.** p99 768µs 는
메시지 간격 10ms 대비 충분히 작아 지연 누적 없음. **§5 P1 은 완결(closed)** 로 갱신한다.

**단, 설계상 한정(중간)**: 이 load runner 는 **closed-loop(요청-응답)** 다. publish 한 메시지를 `ReceiveExactAsync`
로 받은 뒤에야 다음 publish 를 pacing 한다. 즉 publisher 가 subscriber 보다 앞설 수 없어 **송신 큐가 구조적으로
적체되지 않는다.** 그래서 이 테스트는 처리량·지연·누수 0 은 잘 검증하지만 **백프레셔/큐 적체 경로(H1/D012)와
drop-oldest 는 한 번도 실행하지 않는다**(dropped==0 은 부하 때문이 아니라 설계상 당연). "지속 부하에서 큐가
안정" 이라는 목표 해석을 진짜로 stress 하려면, publisher 를 **open-loop(소비자와 무관하게 100Hz 발사)** 로 분리하고
큐 깊이·지연 증가를 측정하는 시나리오가 후속으로 필요하다. 이는 §5 P2(백프레셔 정책 미실행 문제)와 직접 연결된다.
