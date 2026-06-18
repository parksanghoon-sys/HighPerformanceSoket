# phase2-echo-loopback — TCP send pump + 에코 왕복 검토 (~cebf21a)

검토자: Claude / 날짜: 2026-06-11

## 1. 대상

`~1319de1` 이후 ~ `cebf21a` 까지:
- `ca086d4 feat: add saea tcp send pump baseline`
- `da67f91 feat: add default transport factory`
- `89652fe refactor: split transport folder structure` (`Saea/`, `Runtime/` 분리)
- `1319de1 feat: serialize udp endpoint sends` (별도 검토: [r2](./phase2-udp-datagram-r2.md))
- `cebf21a test: add tcp echo loopback coverage` ← 본 검토 핵심

핵심 파일: `src/Hps.Transport/Saea/SaeaTransport.cs`(SendLoopAsync/SendInFlightAsync),
`src/Hps.Transport/Runtime/TransportConnection.cs`(send 시그널/큐), 에코 통합 테스트.

검증: `cebf21a` 체크아웃 상태에서 `dotnet test` 42개 통과(Buffers 18 + Transport 24), 실패·건너뜀 0.

## 2. 요약 판정

**승인. 설계(D007/D009/D016/D017)대로 구현됨.** Phase 2 의 핵심 — listen/connect/accept 수명 +
실제 send/recv 펌프 + **루프백 에코 왕복** + **종료 시 누수 0** — 이 기능적으로 완성됐다.

## 3. 설계 일치 확인

- **D007 (MPSC 큐 → 단일 펌프)**: TCP 송신은 연결당 `_pendingSends` 큐(다중 발행자가 `_sendGate`
  락으로 enqueue = MPSC) → 연결당 단일 `SendLoopAsync` 펌프(`SemaphoreSlim` 시그널)로 직렬화.
  UDP send 도 동일 패턴(endpoint 당 큐+펌프). 소유권 직렬화라는 D007 의 핵심 불변식을 지킨다. ✅
- **D016/D017 (in-flight 완료 release)**: 펌프가 `TryBeginInFlightSend` 로 `InFlightSend` 핸들을
  얻고 `using (inFlight)` 로 감싼다. 정상 송신 후 `Complete()`, 모든 이탈 경로에서 `Dispose()` →
  `ReleaseOnce`(Interlocked 가드)로 **정확히 1회** Transport 소유 ref 반환. close 는 pending 만
  drain, in-flight 는 펌프가 책임 — 이중 release 없음. ✅
- **D009 (소유권 핸드오프)**: 에코 핸들러가 borrowed `TransportReceiveBuffer` 를 콜백 동안 자신의
  `RefCountedBuffer` 로 복사 후 `AddRef`→`TrySend`. 성공 시 가드 ref 만 Release(Transport 가
  나머지 소유), 실패 시 두 ref 모두 Release. D009 의 "가드 ref + 구독자 AddRef, enqueue 실패 시
  전부 Release" 패턴을 교과서적으로 보여준다. D006 의 AddRef-우선 순서도 지킨다. ✅
- **부분 송신 처리**: `SendInFlightAsync` 가 `remaining!=0` 동안 `SendAsync` 반복, `sent==0` 이면
  `ConnectionReset` throw. TCP partial send 를 올바르게 누적 전송. ✅

## 4. 에코 테스트 평가 (cebf21a)

`TcpEcho_WhenReceiveHandlerQueuesResponse_ClientReceivesSamePayload` 는:
- 실제 loopback socket 으로 client→server→(echo handler)→client 왕복을 완성해 **recv 펌프와 send
  펌프가 함께 동작함**을 처음으로 end-to-end 증명한다.
- `WaitForRentedCountAsync(echoPool, 0)` 으로 **send 펌프의 completion-release 경로가 실제 socket
  경로에서 실행됨**(echo 버퍼가 풀로 반환)을 검증한다. PLAN Phase 2 의 "루프백 에코 왕복 + 종료
  버퍼 누수 0" 요구를 정확히 충족. ✅

## 5. 관찰 / 설계 메모 (비차단)

- **O1 (의도된 baseline 편차)**: D007 문구는 "단일 펌프 → SPSC 송신 BipBuffer 채움"이다. baseline
  펌프는 송신 BipBuffer 로 복사하지 않고 `RefCountedBuffer` 블록을 socket 에 **직접** SendAsync 한다.
  이는 D007 의 *정확성 핵심*(MPSC→단일펌프 소유권)은 지키면서 BipBuffer 합치기(throughput
  최적화)만 뒤로 미룬 것이다. baseline 에선 오히려 복사 1회를 아낀다. 주석에도 SAEA/RIO 최적화는
  후속으로 명시. 설계 위반 아님 — Phase 4/5 에서 재평가.
- **O2 (Phase 3 범위, 정상 미구현)**: TCP 프레이밍(D010, 4B 길이 헤더 조립)은 아직 없다. recv 가
  raw chunk 를 전달하고 에코도 chunk 단위다. 에코 테스트는 단일 5바이트 payload(1 chunk 에 수렴)
  라 framing 을 압박하지 않는다. 프레이밍/브로커(D008/D010)는 Phase 3 으로 올바르게 분리됨.
- **O3 (robustness, 경미)**: recv 펌프 `DispatchReceived` 에서 handler 가 예외를 던지면 recv 루프
  Task 가 관측되지 않은 채 종료한다(소켓은 남음). 단 recv 버퍼는 풀 매니지드라 누수는 아니다.
  handler 예외는 계약 위반이므로 baseline 에선 허용 가능하나, Phase 3 dispatch 경계에서 방어 권장.

## 6. 결론

Phase 2(크로스플랫폼 baseline transport)는 **설계 의도대로 완성**됐다: 연결 수명, MPSC→단일펌프
송신, zero-copy UDP recv, in-flight 멱등 release, 에코 왕복, 누수 0. 미구현 항목(SAEA SAEA-pump
최적화, 송신 BipBuffer 합치기, D010 프레이밍, D008 브로커)은 모두 의도된 후속 Phase 범위이며 현
시점 결함이 아니다. 다음은 Phase 3(프레이밍+브로커)로 진입하면 된다.
