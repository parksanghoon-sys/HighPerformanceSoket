# phase3-frame-adapter-command — recv 어댑터 + 명령 디코더 검토 (~6314b7f)

검토자: Claude / 날짜: 2026-06-11
선행: [`phase3-frame-assembler.md`](./phase3-frame-assembler.md)

## 1. 대상

- `d5377bb test: add tcp frame assembler edge coverage` (직전 리뷰 §4 should-add 해소)
- `a66844b feat: add tcp receive frame adapter` (직전 리뷰 C1/C2 통합 해소) — D030
- `6314b7f feat: add tcp command decoder` (신규) — D031

파일: `src/Hps.Protocol/{TcpFrameReceiveHandler.cs, ITcpFrameHandler.cs, TcpCommandDecoder.cs,
TcpCommand.cs, TcpCommandKind.cs, TcpCommandDecodeError.cs}` + 테스트.

검증: `dotnet test` 65개 통과(Buffers 18 + Transport 26 + Protocol 21), 실패·건너뜀 0. 빌드 0/0.

## 2. 요약 판정

**승인. 직전 리뷰의 미결(should-add 테스트 + C1/C2 통합)이 모두 해소됐고, 명령 디코더가
D010/D031 대로 구현됨.**

## 3. 직전 리뷰 항목 해소 확인

- **C1 (assembler ↔ recv 통합)**: `TcpFrameReceiveHandler : ITransportReceiveHandler` 가 connection 별
  `TcpFrameAssembler` 를 `Dictionary` 로 소유. `OnReceived` 가 raw chunk 를 assembler 로 소비하고
  완성 frame 을 `ITcpFrameHandler.OnFrame` 으로 넘긴다. dict 접근만 `_gate` 로 보호하고 assembler
  자체는 connection 당 단일 recv loop 가정에 기대 lock 없이 사용 — Transport 경계와 일치. ✅
- **C2 (PayloadTooLarge → close)**: `CloseConnectionAfterPayloadTooLarge` 가 assembler Dispose +
  `connection.Close()` + handler 통지. D010 "초과 시 끊기" 와 "이후 바이트 오해석 방지"를 정확히 구현. ✅
- **should-add 테스트(§4)**: 전부 추가됨 —
  - 0-length: `TryReadFrame_WhenPayloadLengthIsZero_ReturnsEmptyOwnedFrame`
  - 다중 프레임/한 chunk: `..._WhenMultipleFramesShareOneChunk_ConsumesOnlyFirstFrame`
    (assembler 계약) + 어댑터 `OnReceived_WhenChunksContainMultipleFrames_ForwardsFramesInOrder`
  - maxPayload 정확 경계: `..._WhenPayloadLengthEqualsMax_ReturnsFrame`
  - 분할 청크 무결성: `..._WhenChunksAreFragmentedDeterministically_PreservesAllFramesAndReturnsBuffers`
  - D011 partial release: 어댑터 `OnConnectionClosed_WhenPayloadAssemblyIsIncomplete_ReleasesPartialPayload`

## 4. 명령 디코더 (D031) 평가

- **설계 일치**: `SUBSCRIBE <topic>` / `PUBLISH <topic> <payload>` 를 파싱. topic 은 비어있지 않은
  공백 없는 단일 token, PUBLISH payload 는 둘째 공백 뒤 나머지 전체(빈 payload·내부 공백 허용). D010
  wire 명령과 정확히 일치. ✅
- **zero-copy 소유권**: `TcpCommand` 가 `readonly ref struct` 로 frame span 을 가리킨다(복사 없음).
  힙 저장·async 통과를 컴파일러가 차단해 frame 수명 밖 사용을 막는다. D009 의 "불필요한 관리힙 복사
  금지"와 일관. ✅
- **error-as-flow-control**: malformed 입력을 예외가 아니라 `false` + `TcpCommandDecodeError`(Empty/
  Unknown/MissingTopic/InvalidTopic/MissingPayloadSeparator)로 반환. 적대적 입력에 예외 비용을 물지
  않는 올바른 선택. ✅
- **로직 추적**: command separator 탐색 → 빈 command/미지원 → topic/payload 분리. SUBSCRIBE 의 공백
  거부, PUBLISH 의 payload separator 누락/빈 topic 처리까지 분기 정확. 오프바이원 없음. ✅
- **테스트**: subscribe/publish-payload/empty-payload/malformed 4 메서드(+Theory 로 21 중 다수). 핵심
  분기 커버.

## 5. 관찰 (비차단, 경미)

- **O1 (robustness, low)**: PayloadTooLarge 경로에서 `_frameHandler.OnConnectionClosed` 가 직접 1회
  호출된다. 이중 호출이 안 나는 이유는 Transport recv loop 의 `ObjectDisposedException` 경로가 재통지를
  하지 않기 때문인데, 이는 **다른 프로젝트(Transport)의 구현 세부에 의존**한다. `DisposeAssembler` 는
  멱등(dict remove)이라 partial-payload 이중 release 는 막지만, `OnConnectionClosed` 자체의 이중 호출
  방어는 없다. 향후 recv loop 통지 정책이 바뀌면 깨질 수 있으니, 어댑터에서 connection 별 1회 통지
  가드를 두거나 의존을 주석으로 못 박길 권한다.
- **O2 (handler-throws seam, low)**: `_frameHandler.OnFrame` 이 예외를 던지면 이미 소유권을 넘긴 완성
  frame 이 누수되고 recv loop Task 가 죽는다(계약 위반 경로). dispatch try/catch 로 frame Release 후
  연결 정리하는 방어를 broker wiring 단위에서 고려.
- **O3 (정보)**: D010 의 *랜덤* 적대적 fuzz(대량 프레임)는 여전히 미도입(현재는 결정적 분할 + edge).
  프로토타입 하니스에서 실측됐으므로 우선순위는 낮지만, 영구 회귀로 한 건 추가하면 D010 테스트 목록을
  완전히 닫는다.

## 6. 결론

frame 조립 → recv 어댑터 → 명령 디코드까지 D029/D030/D031 설계대로 이어졌고, 직전 미결은 해소됐다.
명령 디코더는 zero-copy ref struct 로 소유권 경계가 명확하다. 다음은 디코더를 frame handler 에 물려
**브로커 subscription table(D008) + publish fan-out(D007/D009) + protocol error 응답**으로 진입하면 된다.
현 시점 결함 없음.
