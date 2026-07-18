# 혼합 TCP workload 성능 gate 설계

- 날짜: 2026-07-18
- 상태: 사용자 검토 대상
- 결정 후보: D243
- 대상: `tests/Hps.Benchmarks`, benchmark CLI/report, 상태 문서

## 1. 목적

현재 검증 기준인 `4096B x 100 Hz` 단일 stream은 새 운영 목표를 대표하지 못한다.
새 목표는 다음 두 stream을 동시에 처리하는 Interface Server 경로를 검증하는 것이다.

| stream | payload 대역폭 | 최소 발행률 | 100 Hz 기준 payload | topic |
|---|---:|---:|---:|---|
| 주 데이터 | 8.192 Mbps | 100 Hz | 10,240B | `data` |
| 제어·관제 | 2.048 Mbps | 100 Hz | 2,560B | `control` |

100 Hz에서 두 payload를 합치면 초당 1,280,000B, 즉 10.24 Mbps다.
broker fan-out 송신량은 두 topic을 모두 받는 논리 구독자 수를 `N`이라고 할 때 payload 기준 약 `10.24 x N Mbps`다.

`100 Hz 이상`은 무한한 상한이 아니므로 이 설계에서는 다음처럼 해석한다.

- 기본 수락 profile은 주 데이터 10,240B를 100 Hz로 전송한다.
- `--data-rate-hz`를 높이면 payload 크기는 10,240B로 유지하고 주 데이터 대역폭을 비례해 높인다.
- 이는 고정 8.192 Mbps에서 주기만 높아져 메시지가 작아지는 경우보다 보수적인 검증이다.
- 구현 완료 주장은 기본 100 Hz profile까지만 의미한다. 더 높은 운영 상한은 해당 주파수로 별도 실행해야 한다.

## 2. 현재 사실과 제약

- 기존 `BenchmarkTargets`는 4096B, 100 Hz, subscriber 1명, 30초를 전역 기준으로 사용한다.
- 기존 TCP open-loop runner와 raw report는 단일 stream shape다.
- baseline summary/history/envelope는 단일 `payload-bytes`와 `target-rate-hz`를 비교한다.
- TCP frame assembler는 receive chunk보다 큰 payload를 조립할 수 있다.
- `BrokerServer`의 `maxPayloadLength`와 payload pool block은 frame command envelope 전체를 수용해야 한다.
- 현재 UDP receive block은 backend별로 8192B이므로 10,240B 주 데이터 datagram을 수용하지 못한다.
- 일반 MTU 환경의 10KB UDP datagram은 IP fragmentation 위험도 있으므로 이 목표의 첫 수락 경로로 적합하지 않다.
- TCP connection별 pending send queue는 독립적이지만 같은 connection 안의 topic에는 우선순위가 없다.

따라서 첫 검증은 TCP 전용이며, 주 데이터와 제어·관제를 서로 다른 connection으로 분리한다.

## 3. 범위

### 포함

- 기존 benchmark executable 안의 독립 `--mixed-load-open-loop` command.
- 주 데이터와 제어·관제 publisher의 동시 open-loop 전송.
- 논리 구독자마다 topic별로 분리된 TCP subscriber connection.
- SAEA, RIO, io_uring backend selector 재사용.
- stream별 전달 수, payload 무결성, 순서, p50/p99/p999 latency.
- transport drop/HWM, 종료 시 pending send 0, payload pool leak 0.
- 전용 console/JSON raw report와 명시적 pass/fail.

### 제외

- 기존 4096B baseline 상수, report schema, summary/history/envelope 변경.
- UDP receive block 확대, segmentation/reassembly, 신뢰성 또는 순서 보장.
- production Broker/Protocol/Transport hot path 선행 수정.
- topic priority, ACK/retry, durable queue, consumer group.
- 새 benchmark project, 설정 파일 또는 범용 workload graph engine.
- 실제 NIC/스위치가 포함된 원격 네트워크 인증.

## 4. 검토한 접근

### A. 기존 4096B baseline을 새 목표로 교체

장점은 코드 변경이 작다는 것이다.

하지만 기존 TCP/UDP 비교 이력과 raw schema 의미를 바꾸고, UDP가 10,240B를 수용하지 못해 같은 command의 protocol parity도 깨진다.
기존 증거를 잃으므로 채택하지 않는다.

### B. 기존 단일-stream runner에 workload/profile 분기를 추가

CLI command 수는 늘지 않지만 단일 payload/rate/result 모델에 두 stream을 조건부로 넣어야 한다.
baseline reader, summary, history와 envelope까지 혼합 shape를 이해하게 만들 가능성이 높아 D239의 raw report 경계를 흐린다.
채택하지 않는다.

### C. 독립 mixed TCP command와 raw report 추가 - 채택

기존 baseline은 그대로 유지하고 새 목표만 별도 runner/result/report로 검증한다.
backend 생성, `BrokerServer`, protocol command, diagnostics는 기존 경로를 재사용한다.
설정 파일이나 범용 시나리오 엔진을 만들지 않고 필요한 세 옵션만 제공한다.

## 5. 실행 topology

```mermaid
flowchart LR
    DP["data publisher\n10,240B x 100+ Hz"] -->|"PUBLISH data"| B["BrokerServer"]
    CP["control publisher\n2,560B x 100 Hz"] -->|"PUBLISH control"| B
    B -->|"data topic"| DS1["logical subscriber 1\ndata TCP connection"]
    B -->|"control topic"| CS1["logical subscriber 1\ncontrol TCP connection"]
    B -->|"data topic"| DSN["logical subscriber N\ndata TCP connection"]
    B -->|"control topic"| CSN["logical subscriber N\ncontrol TCP connection"]
```

논리 구독자 한 명은 data/control TCP connection을 각각 하나씩 사용한다.
따라서 publisher 2개와 subscriber `2 x N`개, 총 `2 + 2N`개의 client connection이 열린다.

이 구조는 다음을 의도한다.

- data와 control이 같은 pending send queue에서 서로를 막지 않는다.
- 각 stream 안의 순서는 TCP connection 순서로 검증한다.
- 두 stream 사이의 전역 순서는 정의하지 않는다.
- 같은 시각에 두 publisher를 시작해 10ms tick이 겹치는 보수적인 burst를 만든다.

## 6. 고정 workload 계약

### data stream

- topic: `data`
- payload: 10,240B
- 기본 rate: 100 Hz
- payload header: big-endian timestamp 8B + sequence 4B + stream marker 1B
- 나머지 payload: sequence 기반 deterministic pattern

### control stream

- topic: `control`
- payload: 2,560B
- rate: 100 Hz 고정
- payload header와 pattern: data stream과 동일한 shape, 별도 stream marker 사용

### frame 상한

- `BrokerServer` payload pool block과 `maxPayloadLength`: 16,384B
- inbound frame payload는 `PUBLISH <topic> ` command envelope와 stream payload를 포함한다.
- 16KiB는 현재 두 고정 stream을 수용하지만 production max frame 의미를 바꾸지 않는 benchmark-local 값이다.

## 7. CLI 계약

```text
Hps.Benchmarks --mixed-load-open-loop \
  [--backend <saea|rio|iouring>] \
  [--data-rate-hz <100 이상>] \
  [--duration-seconds <1 이상>] \
  [--subscribers <1 이상>] \
  [--report <path>]
```

기본값:

- backend: `saea`
- data rate: 100 Hz
- control rate: 100 Hz
- duration: 30초
- subscribers: 1

검증 규칙:

- `--protocol`은 허용하지 않는다. mixed command는 TCP 전용이다.
- data rate가 100 미만이거나 duration/subscribers가 1 미만이면 usage error다.
- 계획 메시지 수와 계획 전달 수는 `checked` 계산으로 overflow를 거부한다.
- 기존 `--load-open-loop`, `--baseline-suite`, summary/history/envelope command 의미는 바꾸지 않는다.
- 기존 baseline의 latency report-only 정책도 유지하며 5ms/10ms hard gate는 mixed command에만 적용한다.

30분 soak 예시는 다음과 같다.

```text
Hps.Benchmarks --mixed-load-open-loop --duration-seconds 1800 --subscribers 1 --report mixed-soak.json
```

## 8. Runner 구조와 데이터 흐름

1. 선택 backend로 `ITransport`와 16KiB `PinnedBlockMemoryPool`을 만든다.
2. `BrokerServer`를 TCP loopback endpoint에서 시작한다.
3. data/control subscriber connection을 논리 구독자 수만큼 각각 만들고 topic을 구독한다.
4. `WaitForSubscriberCountAsync`로 두 topic 모두 목표 수에 도달했음을 확인한다.
5. subscriber별 receive task를 먼저 시작한다.
6. data/control publisher task는 공통 start signal을 기다린다.
7. 공통 monotonic clock을 시작하고 두 publisher를 동시에 release한다.
8. 각 publisher는 자신의 absolute schedule에 따라 독립 전송한다.
9. 각 subscriber는 자신의 고정 receive buffer를 재사용하며 sequence, marker, pattern과 latency를 기록한다.
10. publisher 완료 뒤 receive drain deadline까지 모든 subscriber의 계획 메시지를 기다린다.
11. endpoint snapshot에서 pending send가 0인지 확인하고 transport diagnostics를 수집한다.
12. server/transport를 종료한 뒤 payload pool `RentedCount == 0`을 확인한다.

benchmark client가 측정에 GC jitter를 만들지 않도록 publisher frame과 subscriber payload buffer는 connection별로 한 번만 만든다.
publisher는 이전 `SendAsync`가 완료된 뒤 timestamp/sequence를 갱신하므로 같은 mutable frame을 안전하게 재사용한다.

## 9. 결과 모델과 raw report

기존 `TcpLoopbackRunResult`와 schema version 1 report는 변경하지 않는다.
mixed command는 전용 result와 전용 JSON writer를 사용한다.

top-level 필드:

- schema version, result/scenario/profile, backend/runner/environment identity
- duration, subscriber count, max frame bytes
- overall passed
- transport drop, TCP pending-send HWM, end pending-send count
- pool rented after stop
- `streams` 배열

stream별 필드:

- name/topic, payload bytes, target/actual rate
- planned/sent message count
- publisher 첫 전송부터 마지막 `SendAsync` 완료까지의 elapsed와 그 값으로 계산한 actual rate
- subscriber count와 planned delivery count
- received delivery count
- minimum/maximum received per subscriber
- failed subscriber count
- payload error count
- p50/p99/p999 latency
- first-half/second-half p99와 growth ratio
- latency budget passed

aggregate received 수만으로는 한 subscriber의 누락을 다른 subscriber가 가릴 수 있으므로 pass 판정은 subscriber별 exact count와 sequence를 사용한다.

mixed raw report는 기존 baseline reader가 읽는 입력 directory에 넣지 않는다.
summary/history/envelope 통합은 실제 반복 비교 요구가 생길 때 별도 단위로 설계한다.

## 10. 수락 조건

### 단일 실행 hard gate

각 stream과 각 subscriber에 대해 모두 만족해야 한다.

- `sent == planned message count`
- `received == planned message count`
- sequence 누락, 중복, 역전과 payload error 0
- 실제 publisher rate가 target의 99% 이상
- p99 latency 5,000us 이하
- p999 latency 10,000us 이하

전체 실행은 다음도 만족해야 한다.

- transport pending-send drop 0
- 종료 직전 모든 TCP endpoint pending send 0
- server/transport 종료 뒤 benchmark가 주입한 fallback payload pool rented 0
- setup, send, receive, drain timeout 0

queue HWM과 first/second-half latency growth는 기록하지만 첫 구현에서는 별도 숫자 hard gate로 만들지 않는다.
drop 0, end pending 0과 latency hard gate가 현재 bounded 안정성 판단을 담당한다.

### backend별 증거

1. Windows SAEA: 기본 profile 30초를 3회 반복한다.
2. Windows RIO: 같은 기본 profile 30초를 3회 반복한다.
3. Linux io_uring: push된 동일 SHA에서 같은 command와 raw artifact를 검증한다.
4. SAEA 또는 배포 우선 backend에서 1,800초 soak를 1회 수행한다.
5. 실제 최대 논리 구독자 수가 확정되면 `--subscribers`에 그 값을 넣은 별도 fan-out gate를 수행한다.

mixed runner가 직접 관측하는 pool count는 `BrokerServer`에 주입한 fallback `PinnedBlockMemoryPool`이다.
RIO/io_uring 내부 receive pool과 registered payload owner cleanup은 기존 backend lifecycle tests와 Linux native artifact를 함께 확인한다.

구독자 수 1의 결과는 single-subscriber 목표만 증명한다.
운영 fan-out 수가 입력되지 않은 상태에서는 다중 구독자 capacity를 충족했다고 주장하지 않는다.

## 11. TDD와 구현 단위

모든 구현은 별도 implementation plan에서 Red/Green/Refactor로 나눈다.

1. options/목표 수학과 overflow validation assertion Red.
2. CLI command/options와 `--protocol` 거부 assertion Red.
3. stream result의 전달·latency·drop·pending·leak pass/fail assertion Red.
4. JSON schema와 stream 배열 assertion Red.
5. 짧은 duration, subscriber 1의 두-stream integration assertion Red.
6. subscriber 2의 fan-out exact delivery assertion Red.
7. reusable client buffer와 공통 start 경계를 유지한 최소 Green.
8. focused tests, benchmark tests, solution build/tests.
9. explicit 30초 반복 gate와 30분 soak.

새 production Broker/Protocol/Transport 변경은 benchmark 결과가 실제 결함을 보여 주기 전에는 추가하지 않는다.

## 12. 실패 처리와 후속 판단

- frame이 거부되면 먼저 16KiB benchmark pool/max frame과 command envelope 계산을 확인한다.
- sent는 맞고 received가 부족하며 drop이 증가하면 subscriber send queue/pump를 조사한다.
- drop 없이 end pending이 남으면 drain 또는 pump 진행을 조사한다.
- payload error가 있으면 framing, sequence 또는 shared buffer mutation을 조사한다.
- delivery는 맞지만 latency가 실패하면 publisher pacing, 같은-process GC, backend completion과 OS scheduling을 분리 측정한다.
- SAEA는 통과하고 native backend만 실패하면 상위 Broker 변경 없이 해당 backend로 범위를 제한한다.

실패 전에는 queue capacity 확대, batching, priority queue, pool 구조 변경을 선행하지 않는다.

## 13. 중요한 미해결 운영 입력

설계와 기본 구현은 다음 값 없이도 진행할 수 있지만, production 목표 완료 주장은 제한된다.

- 실제 최대 주 데이터 발행률: 기본 증거는 100 Hz이며 더 높은 상한은 해당 값으로 실행해야 한다.
- 실제 최대 논리 구독자 수: fan-out NIC 대역폭과 connection 수 gate에 필요하다.
- 제어 데이터 전달 의미: 현재 zero-drop 성능 gate는 프로세스/네트워크 장애에 대한 ACK, retry 또는 durable delivery를 제공하지 않는다.
- 실제 배포 latency SLO: 5ms/10ms는 이번 loopback gate의 초기 기준이며 장비 간 네트워크 예산은 별도다.

이 항목들은 범용 설정 시스템을 미리 만드는 근거가 아니다. 값이 확정되면 같은 CLI 옵션과 raw report로 증거를 추가한다.

## 14. 예상 변경 범위

구현 시 예상되는 범위는 다음과 같다.

- 기존 수정: benchmark command enum/parser/command line, `Program`, run identity.
- 신규: mixed options/targets, mixed TCP runner, mixed result, mixed report writer.
- tests: parser, result/writer 계약, 짧은 mixed integration과 fan-out.
- 상태 문서: 구현·검증 결과와 다음 review stop.

수정 대상이 baseline summary/history/envelope, UDP runner 또는 production project까지 넓어지면 현재 설계를 중단하고 범위를 재검토한다.
