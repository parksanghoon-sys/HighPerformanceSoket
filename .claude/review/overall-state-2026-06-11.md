# overall-state — 전체 구현 종합 검토 (HEAD = 465268d)

검토자: Claude / 날짜: 2026-06-11

## 1. 범위 / 검증

4개 프로젝트 전체: `Hps.Buffers`(Phase 1), `Hps.Transport`(Phase 2),
`Hps.Protocol`(Phase 3 프레이밍/디코드), `Hps.Broker`(Phase 3 라우팅/fanout).

검증: `dotnet build` 0/0, `dotnet test` **77개 통과**(Buffers 18 + Transport 26 + Protocol 23 +
Broker 10), 실패·건너뜀 0.

## 2. 한 줄 판정

**구조적으로 견고하고 컴포넌트별 정확성은 검증됨. 단, (a) 백프레셔 부재, (b) 브로커 연결-종료
구독 정리 부재, (c) end-to-end 미조립 — 이 3가지가 "동작하는 브로커 + 4096B×100Hz" 목표 앞의 핵심
미결이다.** Phase 1~3 코어는 승인, 다음은 위 3건이 최우선.

## 3. Phase 별 상태

| 영역 | 상태 | 비고 |
|---|---|---|
| Phase 1 Buffers | ✅ 완료·견고 | BipBuffer(SPSC, M1/M2 fix), Pool, RefCountedBuffer. fuzz·스트레스 커버 |
| Phase 2 Transport | ✅ baseline 완료 | TCP/UDP 수명, MPSC→펌프→InFlightSend, recv loop, echo 왕복, 동시연결, 누수0 |
| Phase 3 Protocol | ✅ 완료 | 프레임 조립(D010), recv 어댑터(D030, hardened), 명령 디코더(D031) |
| Phase 3 Broker | 🟡 코어만 | SubscriptionTable(D008), BrokerPublisher(D007/D009). 수명연동·백프레셔·서버 미완 |
| Phase 4~7 | ⬜ 미착수 | 벤치마크, SAEA pump 최적화, RIO/io_uring, 튜닝 |

## 4. 교차 강점 (소유권 규율)

`RefCountedBuffer` 의 guard ref + 구독자 AddRef + 정확히-1회 release 규율이 **transport send,
UDP datagram, 프레임 조립, 브로커 fanout 전 계층에서 일관**되게 적용되고 각자 누수 0 테스트로
방어된다. 특히:
- BrokerPublisher 의 fanout 은 guard ref 가 publish 내내 살아 있어 D006 부활 위험 없이 구독자별
  AddRef→TrySend(실패 시 즉시 Release)를 수행한다. **구독자당 payload 복사 0회**(같은 버퍼 공유,
  범위만 전송). D007/D009 정확. ✅
- SubscriptionTable 은 D008 NoCleanup(빈 topic entry 미제거)로 R1 경합(새 구독 유실)을 회피하고
  전용 회귀 테스트로 고정. ✅
- 직전 리뷰 피드백이 모두 반영됨: transport 연결 unregister(M1), close 시 lock 밖 dispose(S1),
  UDP send 직렬화(S2), 프레임 어댑터 hardening(O1 멱등 통지=ConditionalWeakTable, O2 OnFrame
  try/catch+회수), should-add 테스트(0-length/다중프레임/경계).

## 5. 핵심 미결 (우선순위 순)

### H1 (높음) — 백프레셔(D012) 부재: 지속 부하에서 메모리 무한 증가
- 현재 `TransportConnection`/`SaeaUdpEndpoint` 의 pending send 큐는 **상한이 없다**. 느린 소비자가
  있으면 publish fanout 이 큐에 무한 적재되어 메모리가 증가한다.
- 사용자 목표(4096B×100Hz, "지연 누적 없이")는 **지속 부하에서 큐가 안정**돼야 성립한다. 백프레셔
  없이는 이 목표를 만족한다고 말할 수 없다.
- 필요: D012 "drop-oldest"(evict한 RefCountedBuffer 정확히 1회 Release, evict/dequeue/close 단일
  락 직렬화) 또는 "느린 소비자 끊기" 정책 + 큐 길이 ≤ 용량 테스트.

### H2 (높음) — 브로커 연결-종료 구독 정리 부재: churn 시 dead 연결 누적
- `SubscriptionTable` 에 "연결을 모든 topic 에서 제거" API 가 없다(`Unsubscribe`는 topic 지정 단건만).
  연결이 끊겨도 구독 set 에 dead `IConnection` 참조가 남는다.
- payload 누수는 없다(closed 연결에 TrySend→false→Release). 그러나 **dead 연결 참조 자체가 누적**되어
  단명 연결 churn 서버에서 메모리가 증가하고 `CountSubscribers` 가 부풀려진다(transport M1 과 동형 문제).
- 필요: 연결 종료 시 모든 topic 에서 unsubscribe 하는 경로 + API. 아래 H3 와 함께 wiring.

### H3 (중간) — end-to-end 미조립: 실행 가능한 pub/sub 서버 없음
- 브로커 조각(table + publisher)은 있으나, **명령을 decode 해 Subscribe/Publish 를 호출하는
  `ITcpFrameHandler` 구현**, `Hps.Server` 호스트, 샘플 publisher/subscriber 가 없다.
- 즉 `recv chunk → 프레임 → 명령 디코드 → 브로커 라우팅/fanout` 의 마지막 결선이 빠져 있어 아직
  통합 동작을 돌릴 수 없다. H2 의 disconnect 정리도 이 결선에서 자연히 연결된다.

### L1 (낮음) — D008 topic entry 영구 누적
- NoCleanup tradeoff 로 distinct topic 키가 영구 누적(주기적 안전 sweep 미구현). churn 이 아닌
  distinct-topic 수에 비례하므로 우선순위는 낮음. 필요 시 sweep 도입.

### L2 (낮음) — 잔여 baseline 항목
- SAEA `SocketAsyncEventArgs` pump 최적화·송신 BipBuffer 합치기(Phase 5 전), RIO/io_uring(Phase
  5/6), Phase 4 벤치(4096B×100Hz 정량화), D010 랜덤 fuzz 영구 회귀 — 모두 의도된 후속.

## 6. 결론 / 권고

Phase 1~3 코어는 설계(DECISIONS)대로 정확하고 테스트로 잘 방어된다. 다음 작업 순서를 권한다:
1. **H3 end-to-end 결선**(프레임핸들러→브로커) — 이때 **H2 disconnect 정리**를 함께 넣는다.
2. **H1 백프레셔(D012)** — 지속 부하 목표의 전제. 결선 직후 착수.
3. 그 위에서 **Phase 4 벤치**로 4096B×100Hz 를 정량 게이트로 고정.

현 시점 코드 결함(버그)은 없다. 위 미결은 "다음 구현 범위"이지 기존 코드의 오류가 아니다.
