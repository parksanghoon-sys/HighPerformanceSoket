# Interface Server 사용 가이드

이 문서는 현재 코드베이스 기준으로 Interface Server 를 실제로 실행하고, 외부 source / subscriber endpoint 를
어떻게 붙이는지 설명한다.

현재 안정적으로 사용할 수 있는 public 사용 경로는 다음 세 가지다.

- 샘플 broker server 를 실행하고 TCP publisher/subscriber 샘플로 확인한다.
- WPF dashboard 로 TCP/UDP smoke 와 transport diagnostics 를 확인한다.
- 애플리케이션 코드에서 `BrokerServer`를 직접 만들고 `StartTcpAsync` / `StartUdpAsync`로 endpoint 를 연다.

`io_uring` registered payload pool 과 TCP payload `WRITE_FIXED` 경로는 구현됐고,
Linux 원격 계약 테스트에서 실제 fixed payload hit까지 확인됐다.
이 경로는 별도 fixed-write 옵션이 아니라 `BrokerServer`에 `IoUringTransport`를 주입하면 내부적으로 선택된다.
다만 기본 transport와 sample broker CLI는 아직 SAEA/RIO만 선택하므로, Linux에서 사용하려면 아래 opt-in 경로를 명시적으로 사용한다.

## 1. 빠른 실행: TCP broker + subscriber + publisher

PowerShell 터미널을 3개 연다.

### 1-1. Broker server 실행

```powershell
dotnet run --project samples\Hps.Sample.BrokerServer\Hps.Sample.BrokerServer.csproj -- 127.0.0.1 5000 65536
```

의미:

- `127.0.0.1`: loopback 에서만 받는다.
- `5000`: TCP listen port 다.
- `65536`: TCP frame payload 최대 크기다. 이 값은 `PinnedBlockMemoryPool` block size 로도 쓰인다.
- `--transport`를 생략하면 SAEA transport 를 사용한다.

RIO를 명시적으로 시도하려면 Windows에서 다음처럼 실행한다.

```powershell
dotnet run --project samples\Hps.Sample.BrokerServer\Hps.Sample.BrokerServer.csproj -- 127.0.0.1 5000 65536 --transport auto
```

`auto`는 현재 host/capability 에서 RIO를 쓸 수 있으면 RIO를 선택하고, 아니면 SAEA로 fallback 한다.
Linux `io_uring` backend 는 아직 이 샘플 broker server 의 일반 실행 옵션으로 승격하지 않았다.

### 1-2. Subscriber 실행

두 번째 터미널에서 topic `alpha`를 구독한다.

```powershell
dotnet run --project samples\Hps.Sample.Subscriber\Hps.Sample.Subscriber.csproj -- 127.0.0.1 5000 alpha
```

subscriber 는 `SUBSCRIBE alpha` command 를 TCP length-prefixed frame 으로 broker 에 보낸 뒤,
broker 가 fan-out 하는 payload frame 을 계속 출력한다.

### 1-3. Publisher 실행

세 번째 터미널에서 같은 topic 으로 payload 를 발행한다.

```powershell
dotnet run --project samples\Hps.Sample.Publisher\Hps.Sample.Publisher.csproj -- 127.0.0.1 5000 alpha "hello interface server"
```

subscriber 터미널에 다음 내용이 출력되면 TCP pub/sub 경로가 동작한 것이다.

```text
hello interface server
```

## 2. WPF dashboard 로 확인

Windows에서 WPF dashboard 를 실행한다.

```powershell
dotnet run --project samples\Hps.Sample.Dashboard\Hps.Sample.Dashboard.csproj
```

확인 흐름:

- `Start server`: dashboard 내부 broker server 를 loopback TCP/UDP endpoint 에 bind 한다.
- `TCP smoke`: 임시 loopback broker 를 만들어 TCP `SUBSCRIBE` / `PUBLISH` fan-out 을 실제 socket 으로 확인한다.
- `UDP smoke`: 임시 loopback broker 를 만들어 UDP datagram `SUBSCRIBE` / `PUBLISH` fan-out 을 실제 socket 으로 확인한다.
- diagnostics grid: drop count, pending count, send queue high-watermark 를 확인한다.

주의:

- `TCP smoke` / `UDP smoke`는 `Start server`로 띄운 dashboard server 를 직접 때리는 버튼이 아니다.
  각 smoke 는 자체 임시 server 를 만들어 protocol 경로를 검증한다.
- dashboard 의 `io_uring` evidence 는 Windows 앱에서 직접 수행하지 않는다.
  Linux native path 는 GitHub Actions `iouring-linux-contract.yml` artifact 로 확인한다.

## 3. 애플리케이션 코드에 직접 임베드

외부 source 시스템과 같은 process 안에 Interface Server 를 붙일 때는 `BrokerServer`를 직접 만든다.

현재 repository source project를 직접 참조하는 host라면 project 위치에 맞춰 다음 참조를 추가한다.

```xml
<ItemGroup>
  <ProjectReference Include="..\..\src\Hps.Buffers\Hps.Buffers.csproj" />
  <ProjectReference Include="..\..\src\Hps.Server\Hps.Server.csproj" />
  <ProjectReference Include="..\..\src\Hps.Transport\Hps.Transport.csproj" />
</ItemGroup>
```

아래 기본 예제는 크로스플랫폼 기준선인 `SaeaTransport`로 TCP와 UDP endpoint를 함께 연다.

```csharp
using System;
using System.Net;
using System.Threading.Tasks;
using Hps.Buffers;
using Hps.Server;
using Hps.Transport;

namespace MyInterfaceServerHost
{
    internal static class Program
    {
        public static async Task Main()
        {
            int maxPayloadBytes = 65536;

            using (ITransport transport = new SaeaTransport())
            {
                PinnedBlockMemoryPool pool = new PinnedBlockMemoryPool(maxPayloadBytes);

                using (BrokerServer server = new BrokerServer(transport, pool, maxPayloadBytes))
                {
                    await server.StartTcpAsync(new IPEndPoint(IPAddress.Loopback, 5000)).ConfigureAwait(false);
                    await server.StartUdpAsync(new IPEndPoint(IPAddress.Loopback, 5001)).ConfigureAwait(false);

                    Console.WriteLine("TCP: {0}", server.LocalEndPoint);
                    Console.WriteLine("UDP: {0}", server.UdpLocalEndPoint);
                    Console.WriteLine("종료하려면 Enter 를 누르십시오.");
                    Console.ReadLine();

                    await server.StopAsync().ConfigureAwait(false);
                }
            }
        }
    }
}
```

이 방식에서 애플리케이션은 다음 역할만 맡는다.

- 어떤 transport 를 쓸지 선택한다.
- broker 가 받을 TCP/UDP endpoint 를 연다.
- 외부 시스템은 TCP frame 또는 UDP datagram 으로 `SUBSCRIBE` / `PUBLISH` command 를 보낸다.
- 서버 종료 시 `StopAsync` 또는 `Dispose` 경로를 지나 listener, endpoint, send queue, buffer ref 를 정리한다.

### 3-1. Linux io_uring을 명시적으로 사용

Linux에서 `io_uring` backend를 직접 사용하려면 host project에 다음 참조도 추가한다.

```xml
<ProjectReference Include="..\..\src\Hps.Transport.IoUring\Hps.Transport.IoUring.csproj" />
```

그다음 transport 생성 부분을 capability probe 기반으로 선택한다.

```csharp
ITransport transport;
IoUringCapabilityStatus status = IoUringCapabilityProbe.GetStatus();

if (status == IoUringCapabilityStatus.Available)
    transport = new IoUringTransport();
else
    transport = new SaeaTransport();

using (transport)
{
    PinnedBlockMemoryPool pool = new PinnedBlockMemoryPool(maxPayloadBytes);

    using (BrokerServer server = new BrokerServer(transport, pool, maxPayloadBytes))
    {
        await server.StartTcpAsync(new IPEndPoint(IPAddress.Loopback, 5000)).ConfigureAwait(false);
        await server.StartUdpAsync(new IPEndPoint(IPAddress.Loopback, 5001)).ConfigureAwait(false);

        Console.ReadLine();
        await server.StopAsync().ConfigureAwait(false);
    }
}
```

`IoUringTransport`를 주입한 TCP host는 `BrokerServer`의 backend-neutral payload source provider 경계를 통해
registered payload block을 우선 사용한다. registered slot이 가득 찬 경우에는 기존 pinned pool로 fallback하며,
send 시 registered block hit이면 `WRITE_FIXED`를 먼저 시도한다.

`TransportFactory.CreateDefault()`는 여전히 `SaeaTransport`를 반환한다.
따라서 `io_uring` 사용 여부와 capability unavailable 시 fallback 정책은 host 조립 계층에서 명시적으로 결정해야 한다.

## 4. TCP wire protocol

TCP는 stream 이므로 모든 command 와 fan-out payload 를 frame 으로 감싼다.

```text
4-byte big-endian payload length
payload bytes
```

client 가 broker 로 보내는 frame payload 는 ASCII command 다.

```text
SUBSCRIBE <topic>
UNSUBSCRIBE <topic>
PUBLISH <topic> <payload bytes>
REGISTER <subscriber-id>
UNREGISTER <subscriber-id>
```

예:

```text
SUBSCRIBE alpha
PUBLISH alpha hello
```

broker 가 TCP subscriber 로 보내는 frame payload 는 command 가 아니라 publish payload 자체다.
즉 subscriber 는 length-prefixed frame 을 읽고, payload bytes 를 애플리케이션 데이터로 해석하면 된다.

topic 과 subscriber id 는 공백 없는 printable ASCII token 이어야 한다.
payload 는 `PUBLISH <topic> ` 뒤의 나머지 byte 전체다.

## 5. UDP wire protocol

UDP는 datagram 하나가 message 하나다. 별도 length prefix 를 붙이지 않는다.

client 가 broker 로 보내는 datagram payload 는 TCP command 와 같은 ASCII command 형식이다.

```text
SUBSCRIBE <topic>
UNSUBSCRIBE <topic>
PUBLISH <topic> <payload bytes>
REGISTER <subscriber-id>
UNREGISTER <subscriber-id>
```

broker 가 UDP subscriber 로 보내는 datagram payload 는 publish payload 자체다.
TCP처럼 outbound frame header 를 붙이지 않는다.

UDP subscriber 는 먼저 같은 local socket 또는 remote endpoint 조합으로 `SUBSCRIBE <topic>` datagram 을 broker 에 보내야 한다.
이후 다른 publisher 가 같은 topic 으로 `PUBLISH` datagram 을 보내면 broker 가 subscriber remote 로 payload 를 fan-out 한다.

## 6. Stable subscriber identity 선택 기능

subscriber 가 재연결 후에도 이전 topic set 을 복구해야 하면 stable identity 를 켤 수 있다.

```csharp
BrokerServerOptions options =
    BrokerServerOptions.CreateWithStableSubscriberIdentity(
        TimeSpan.FromMinutes(5),
        TimeProvider.System);

using (BrokerServer server = new BrokerServer(transport, pool, maxPayloadBytes, options))
{
    await server.StartTcpAsync(new IPEndPoint(IPAddress.Loopback, 5000)).ConfigureAwait(false);
}
```

client 흐름:

```text
REGISTER device-a
SUBSCRIBE alpha
```

이후 같은 `device-a`가 새 TCP connection 또는 새 UDP remote 에서 다시 `REGISTER device-a`를 보내면,
보존된 topic metadata 를 새 runtime target 으로 재바인딩한다.
payload replay 는 하지 않는다. 재등록 이후 들어오는 새 publish 부터 받는다.

명시적으로 identity 와 topic metadata 를 버리려면 다음 command 를 보낸다.

```text
UNREGISTER device-a
```

## 7. UDP lease sweep 선택 기능

UDP는 TCP처럼 연결 종료 이벤트가 없으므로, 오래 activity 가 없는 UDP remote 를 자동 정리하고 싶으면 lease sweep 을 켠다.

```csharp
BrokerServerOptions options =
    BrokerServerOptions.CreateWithUdpLeaseSweep(
        TimeSpan.FromMinutes(1),
        TimeSpan.FromSeconds(10),
        TimeProvider.System);

using (BrokerServer server = new BrokerServer(transport, pool, maxPayloadBytes, options))
{
    await server.StartUdpAsync(new IPEndPoint(IPAddress.Loopback, 5001)).ConfigureAwait(false);
}
```

stable identity 와 UDP lease sweep 을 같이 쓰려면 기존 options 에 stable identity 를 추가한다.

```csharp
BrokerServerOptions options =
    BrokerServerOptions
        .CreateWithUdpLeaseSweep(TimeSpan.FromMinutes(1), TimeSpan.FromSeconds(10), TimeProvider.System)
        .WithStableSubscriberIdentity(TimeSpan.FromMinutes(5));
```

## 8. 실제 연동 시 권장 구조

외부 정보를 받아 각 endpoint 로 발행하는 Interface Server 로 사용할 때의 기본 구조는 다음과 같다.

```text
외부 source adapter
  -> topic 결정
  -> TCP 또는 UDP PUBLISH command 생성
  -> BrokerServer endpoint 로 전송
  -> Broker fan-out
  -> TCP/UDP subscriber endpoint 로 payload 전달
```

권장 사항:

- source adapter 는 topic naming 과 payload encoding 만 책임진다.
- subscriber 는 먼저 `SUBSCRIBE <topic>`을 보내고, 이후 수신 payload 를 업무 데이터로 처리한다.
- TCP subscriber 는 항상 4-byte big-endian length prefix 를 읽는다.
- UDP subscriber 는 datagram payload 를 그대로 읽는다.
- 느린 subscriber 가 있으면 transport send queue 는 bounded drop-oldest 정책을 사용한다.
  따라서 최신 데이터 우선 stream 에 적합하고, 모든 payload 를 반드시 보존해야 하는 persistence/replay 용도는 아직 범위 밖이다.

## 9. 현재 제한

- TLS, 인증, persistence, replay, clustering 은 아직 제공하지 않는다.
- UDP 신뢰성, 순서 보장, 혼잡 제어는 범위 밖이다.
- Linux `io_uring` backend 는 TCP/UDP opt-in public transport로 사용할 수 있지만,
  일반 sample broker CLI나 `TransportFactory.CreateDefault()`의 기본 backend로 승격하지 않았다.
- io_uring TCP registered payload의 `WRITE_FIXED` hit는 Linux 계약 테스트로 확인됐지만,
  TCP receive chunk에서 owned publish payload block으로 옮기는 1회 복사는 유지된다.
  따라서 이 결과를 end-to-end zero-copy 달성이나 latency hard gate 통과로 해석하지 않는다.
