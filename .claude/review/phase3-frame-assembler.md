# phase3-frame-assembler — TCP 프레임 조립기 baseline 검토 (~6758105)

검토자: Claude / 날짜: 2026-06-11

## 1. 대상

- `6a5c768 test: add tcp concurrent echo coverage` (Phase 2 N-연결 안정성 마무리)
- `6758105 feat: add tcp frame assembler baseline` ← 본 검토 핵심 (Phase 3 / D010 시작)

파일: 신규 `src/Hps.Protocol/{TcpFrameAssembler.cs, TcpFrameReadStatus.cs}`,
`tests/Hps.Protocol.Tests/TcpFrameAssemblerTests.cs`, slnx 등록.

검증: `dotnet test` 47개 통과(Protocol 3 + Buffers 18 + Transport 26), 실패·건너뜀 0. 빌드 0/0.

## 2. 요약 판정

**승인. D010 설계대로 구현됨.** TCP length-prefix 프레임 조립 상태머신이 헤더 분할·payload 누적
복사·소유권 이전·종료 release 를 정확히 처리한다. 아래는 모두 should-add(테스트 보강)와 통합
단위로 넘길 항목이며 현 결함 아님.

## 3. 설계 일치 확인 (D010 / D009 / D011)

- **헤더 4B 분할 누적**: `ReadHeader` 가 `_header` 에 `_headerBytesRead` 오프셋으로 chunk 경계를
  넘어 누적, 4B 완성 시 `BinaryPrimitives.ReadInt32BigEndian` 파싱. 테스트가 1+2+1 바이트로 헤더를
  3 chunk 에 쪼개 검증. ✅
- **payload 1회 복사(D009/D010)**: `ReadPayload` 가 chunk 의 payload 조각을 소유권 있는
  `RefCountedBuffer.Span` 에 누적 복사. 각 바이트 정확히 1회 복사. recv chunk 가 payload 보다 작아도
  여러 호출에 걸쳐 누적(테스트의 chunk 1~3B < payload 5B). ✅
- **maxPayload DoS 가드**: `payloadLength > _maxPayloadLength` 면 buffer 대여 없이 `PayloadTooLarge`
  반환. 생성자에서 `maxPayloadLength ≤ pool.BlockSize` 강제. 테스트로 초과 시 대여 0 검증. ✅
- **소유권 이전**: `CompleteFrame` 이 `_payload`(refcount=1)를 caller 에 반환하고 `_payload=null`.
  assembler 는 Release 하지 않는다 → publish 가 그 버퍼를 그대로 fan-out 소유 단위로 쓸 수 있다(D009). ✅
- **D011 조립중 release**: `Dispose` → `ReleasePartialPayload` 가 조립 중 `_payload` 를 Release.
  idempotent(이중 Dispose 안전). 테스트로 mid-payload Dispose 후 `RentedCount==0` 검증. ✅
- **0-length frame**: `_expectedPayloadLength==0` 경로가 즉시 `CompleteFrame`(length 0 버퍼). 코드상 정상.
- **상태 전이 로직**: `TryReadFrame` 루프가 헤더 완성→payload 를 같은 호출에서 이어 처리하고,
  프레임 완성 시 즉시 `FrameReady` 반환(consumed 로 소비량 보고). 부분 헤더/부분 payload 모두
  `NeedMoreData` 로 안전 종료. 추적 결과 누수·오프바이원 없음. ✅

또한 `6a5c768` 의 8-연결 동시 echo 테스트가 PLAN Phase 2 의 "동시 연결 N개 안정성 + 누수 0"
요구를 충족해 Phase 2 baseline 을 마감한다.

## 4. should-add (테스트 보강, 비차단)

D010 가 요구한 테스트 목록 대비 현재 3건은 핵심 경로만 덮는다. 다음을 보강 권장:
- **0-length frame** 정상 완성 경로(코드는 처리하나 테스트 없음).
- **한 chunk 에 다중 프레임** 연속 → caller 재호출 루프(현 계약은 "1 호출 1 프레임 + consumed").
  이 계약을 테스트로 고정하지 않으면 통합 시 두 번째 프레임 누락 회귀를 놓친다.
- **maxPayload 정확 경계**(payloadLength == maxPayloadLength 성공) — 현재는 초과만 테스트.
- **D010 적대적 fuzz**(랜덤 chunk·recv<payload·maxPayload 경계·다중 프레임, 대량 무결성). PLAN
  Phase 3 가 명시적으로 요구. 프로토타입 하니스(`phase3-framing-and-close.md`)에서 실측했으나
  영구 회귀 테스트로는 아직 없다.

## 5. 통합 시 확인 필요 (다음 단위)

- **C1 — 미연결 상태**: assembler 는 독립 유닛(`Hps.Protocol`)이며 아직 TCP recv 루프에 연결되지
  않았다. D011 의 "연결 close 시 조립중 release" 는 assembler `Dispose` 로 구현돼 있으나, 실제
  `TransportConnection.Close` 가 per-connection assembler 를 소유·Dispose 하도록 묶는 통합이 다음
  단위에 필요하다. (현 D013 소단위 분리상 정상)
- **C2 — PayloadTooLarge 후 정책**: assembler 는 too-large 시 header 상태로 reset 만 하고 스트림을
  계속 파싱할 수 있다. D010 계약("초과 시 끊기")대로 **통합 계층이 PayloadTooLarge 를 받으면 연결을
  닫아야** 한다. 닫지 않고 재호출하면 이후 바이트를 새 헤더로 오해석한다. 통합에서 close 강제 필요.
- **C3 — 최대 프레임 = 블록 크기**: `maxPayloadLength ≤ BlockSize` 라 단일 payload 가 풀 블록 하나에
  담긴다(RefCountedBuffer=1 블록, D009 와 일관). 4096B 목표 충족하려면 풀 BlockSize ≥ 4096 으로
  잡아야 한다. 통합/구성 시 명시 권장.

## 6. 결론

프레임 조립기는 D010 의도대로 정확히 구현됐고, 동시 echo 로 Phase 2 가 마감됐다. 남은 것은
(a) D010 테스트 보강(0-length·다중프레임·경계·fuzz), (b) assembler↔recv 루프 통합(C1/C2/C3),
(c) 이후 브로커 라우팅(D008)·백프레셔(D012)다. 모두 의도된 후속 범위이며 현 시점 결함은 없다.
