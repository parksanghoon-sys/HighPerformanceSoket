# Hps.Sample.Dashboard

WPF 기반 Interface Server 확인용 샘플이다.

## 실행

```powershell
dotnet run --project samples\Hps.Sample.Dashboard\Hps.Sample.Dashboard.csproj
```

## 확인 항목

- `Start server`: TCP/UDP broker endpoint 를 loopback 에 bind 한다.
- `TCP smoke`: TCP `SUBSCRIBE`/`PUBLISH` fan-out 을 실제 socket 으로 확인한다.
- `UDP smoke`: UDP datagram `SUBSCRIBE`/`PUBLISH` fan-out 을 실제 socket 으로 확인한다.
- diagnostics grid: transport drop/high-watermark/pending 값을 표시한다.

`TCP smoke`와 `UDP smoke`는 Start server와 별개로 임시 loopback server 를 만들어 수행하는 독립 검증이다.
따라서 smoke 결과는 protocol fan-out 동작 확인용이고, `Start server`로 띄운 dashboard server 의 endpoint 를 직접 때리는 작업은 아니다.

`io_uring` fixed-buffer evidence 는 Windows WPF 앱에서 직접 실행하지 않는다.
Linux native path 는 원격 `iouring-linux-contract.yml` artifact gate 로 확인한다.
