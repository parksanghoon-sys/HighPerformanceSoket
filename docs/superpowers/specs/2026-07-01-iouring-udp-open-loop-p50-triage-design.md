# io_uring UDP open-loop p50 반복 signal triage 설계

- 날짜: 2026-07-01
- 상태: Accepted
- 관련 결정: D153, D154, D155, D156, D157, D158
- 관련 baseline: `docs/benchmarks/baselines/runners/ci-linux-iouring-x64-01/udp/`

## 목적

D157에서 UDP open-loop `p50-median-us` envelope signal 이 3개 reference-present candidate 모두에서 반복됐다.
이번 설계는 이 반복 signal 을 transport 최적화로 볼지, benchmark/reference 안정화 문제로 볼지 판단하고
다음 최소 작업을 정한다.

## 관측값

reference run `28492234252`의 UDP open-loop raw p50/p99:

| raw report | p50 us | p99 us | growth |
| --- | ---: | ---: | ---: |
| open-loop-01 | 154.2 | 1273.4 | 1.00 |
| open-loop-02 | 158.6 | 1276.1 | 1.00 |
| open-loop-03 | 1229.0 | 1322.0 | 1.02 |

reference-present candidates:

| run id | raw p50 us values | p99 max us | UDP envelope signals |
| --- | --- | ---: | --- |
| 28493590950 | 1156.3, 1252.2, 1135.8 | 1312.4 | load `p99-max-us`, open-loop `p50-median-us` |
| 28494135787 | 1232.2, 1228.9, 1146.0 | 1270.3 | load `p99-growth-ratio-max`, open-loop `p50-median-us` |
| 28494404015 | 1231.4, 1229.8, 1229.8 | 1271.3 | open-loop `p50-median-us` |

모든 candidate 는 workflow success, UDP hard-passed true, dropped total 0, payload-error total 0, pool-rented max 0이다.

## 해석

반복 signal 의 핵심은 open-loop p50 median 이다.
하지만 reference session 의 p50은 2개 raw run 이 160 us 이하이고 1개 raw run 은 1229 us다.
반면 이후 9개 candidate raw run 은 모두 1135~1252 us 범위다.
즉 candidate 가 갑자기 느려졌다기보다, 첫 provisional reference 가 bimodal/얇은 표본이라 p50 envelope 를 지나치게 낮게 잡은 것으로 보는 편이 더 타당하다.

p99는 reference 와 candidate 모두 대체로 1.27~1.32 ms 범위다.
drop, payload error, pool leak, UDP send queue HWM 증가도 없다.
따라서 현재 evidence 만으로 io_uring UDP pump correctness 문제나 fixed registration/zero-copy 필요성을 주장하지 않는다.

## 검토한 선택지

### 선택지 A: io_uring UDP send/receive 최적화를 바로 시작

채택하지 않는다.
signal 은 p50 중앙값 envelope 문제이며 hard gate 실패가 아니다.
transport 최적화 전에 reference 품질을 먼저 안정화해야 한다.

### 선택지 B: envelope threshold 만 완화

채택하지 않는다.
threshold 완화는 증상을 숨기지만 reference 표본이 얇다는 근본 문제를 해결하지 않는다.

### 선택지 C: UDP provisional reference 에 candidate sessions 를 수동 채택해 baseline 을 안정화

채택한다.
candidate 3개는 모두 hard gate 를 통과했고, 반복적으로 비슷한 open-loop p50 범위를 보인다.
이 raw report 를 UDP protocol root 의 추가 provisional session 으로 수동 채택하면,
history envelope 가 단일 session 의 낮은 p50 outlier 에 휘둘리지 않게 된다.

## 결정

D158: D155~D157 UDP candidate raw reports 를 `ci-linux-iouring-x64-01/udp/2026-07-01/session-02..04`로 수동 채택해
UDP provisional reference 를 안정화한다.

- run `28493590950` UDP raw report 6개 -> `session-02`
- run `28494135787` UDP raw report 6개 -> `session-03`
- run `28494404015` UDP raw report 6개 -> `session-04`
- summary/history 는 repository 경로 기준으로 재생성한다.
- `docs/benchmarks/baselines/index.md`에 session rows 와 updated UDP provisional envelope 를 기록한다.
- candidate raw report 는 자동 채택이 아니라 D157 triage 결과에 따른 수동 채택이다.
- TCP protocol root 는 이번 범위에서 확장하지 않는다. 반복 signal 은 UDP에서만 발생했기 때문이다.

## 다음 작업

1. 각 artifact 의 UDP raw report 6개만 target session directory 로 복사한다.
2. `--summarize-baseline`으로 `session-02..04` summary JSON/Markdown을 재생성한다.
3. UDP date root history 와 protocol root history 를 재생성한다.
4. `--compare-baseline-envelope` smoke 로 updated UDP reference history 가 candidate 를 과도하게 signal 하지 않는지 확인한다.
5. index/state/decision/changelog 문서에 updated provisional reference 값을 기록한다.

## 범위 밖

- TCP protocol root 확장
- automatic baseline adoption
- latency hard gate 또는 warning-as-failure
- envelope threshold 변경
- fixed payload registration cache
- zero-copy send
- UDP pump 구조 변경
- `TransportFactory.CreateDefault()` promotion

## 검증 계획

- raw report count 가 session 별 6개인지 확인한다.
- regenerated UDP history 가 session-count 4, hard-passed true, comparison-compatible true 인지 확인한다.
- absolute path 가 repository baseline artifact 에 들어가지 않았는지 확인한다.
- updated reference 로 envelope command smoke 를 실행한다.
- `git diff --check`와 benchmark focused tests 를 실행한다.
