using System;
using System.Reflection;
using System.Runtime.InteropServices;
using Xunit;

namespace Hps.Transport.IoUring.Tests
{
    public sealed class IoUringRegisteredBufferSetTests
    {
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
    }
}
