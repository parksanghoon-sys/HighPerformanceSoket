using System;
using System.Reflection;
using System.Runtime.InteropServices;
using Hps.Transport;
using Xunit;
using Xunit.Abstractions;

namespace Hps.Transport.IoUring.Tests
{
    public sealed class IoUringRegisteredBufferSetTests
    {
        private readonly ITestOutputHelper _output;

        public IoUringRegisteredBufferSetTests(ITestOutputHelper output)
        {
            _output = output ?? throw new ArgumentNullException(nameof(output));
        }

        // fixed buffer registration owner 는 pool block 수명과 kernel registration 수명을 분리하는 경계다.
        // 타입 존재를 먼저 assertion failure 로 고정해 production boundary 가 아직 없음을 Red 로 확인한다.
        [Fact]
        public void IoUringRegisteredBufferSet_TypeExists()
        {
            Type? type = Type.GetType("Hps.Transport.IoUringRegisteredBufferSet, Hps.Transport.IoUring");

            Assert.NotNull(type);
        }

        // non-Linux 에서는 registration 을 시도하면 syscall 로 들어가지 않고 명시적으로 막아야 한다.
        // queue 가 null 이어도 platform guard 가 먼저 동작해야 Windows 개발 환경에서 native 호출 위험이 없다.
        [Fact]
        public void Register_WhenNotLinux_ThrowsNotSupportedException()
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
                return;

            Type? type = Type.GetType("Hps.Transport.IoUringRegisteredBufferSet, Hps.Transport.IoUring");
            Assert.NotNull(type);

            MethodInfo? method = type!.GetMethod("Register", BindingFlags.Static | BindingFlags.NonPublic);
            Assert.NotNull(method);

            TargetInvocationException exception = Assert.Throws<TargetInvocationException>(delegate()
            {
                method!.Invoke(null, new object?[] { null, Array.Empty<byte[]>() });
            });

            Assert.IsType<NotSupportedException>(exception.InnerException);
        }

        // 원격 Linux contract artifact 에는 실제로 몇 개의 fixed buffer 를 등록했는지 드러나야 한다.
        // 이 값이 없으면 register/unregister 성공 여부만 보이고, 후속 fixed-buffer pump 가 기대하는 table 크기 계약을 검증할 수 없다.
        [Fact]
        public void IoUringRegisteredBufferSet_WhenInspected_ExposesRegisteredBufferCount()
        {
            Type? type = Type.GetType("Hps.Transport.IoUringRegisteredBufferSet, Hps.Transport.IoUring");
            Assert.NotNull(type);

            PropertyInfo? property = type!.GetProperty("RegisteredBufferCount", BindingFlags.Instance | BindingFlags.NonPublic);

            Assert.NotNull(property);
        }

        // Linux capability available 환경에서는 fixed buffer register/unregister 를 실제 syscall 로 검증한다.
        // pump 에 연결하기 전 native owner 단독으로 실패 지점을 분리하기 위한 evidence test 이며, unavailable 환경은 실패로 보지 않는다.
        [Fact]
        public void Register_WhenLinuxCapabilityAvailable_RegistersAndUnregistersMultipleBuffers()
        {
            IoUringCapabilityStatus status = IoUringCapabilityProbe.GetStatus();
            _output.WriteLine("io_uring capability status: " + status);

            if (status != IoUringCapabilityStatus.Available)
                return;

            using (IoUringQueue queue = IoUringQueue.CreateForProbe(4))
            using (IoUringRegisteredBufferSet registration = IoUringRegisteredBufferSet.Register(
                queue,
                new byte[][]
                {
                    new byte[64],
                    new byte[128]
                }))
            {
                int registeredBufferCount = ReadRegisteredBufferCount(registration);
                _output.WriteLine("registered fixed buffer count: " + registeredBufferCount);

                Assert.Equal(2, registeredBufferCount);
            }
        }

        private static int ReadRegisteredBufferCount(IoUringRegisteredBufferSet registration)
        {
            PropertyInfo? property = typeof(IoUringRegisteredBufferSet).GetProperty(
                "RegisteredBufferCount",
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.NotNull(property);

            object? value = property!.GetValue(registration);
            Assert.NotNull(value);
            return (int)value!;
        }
    }
}
