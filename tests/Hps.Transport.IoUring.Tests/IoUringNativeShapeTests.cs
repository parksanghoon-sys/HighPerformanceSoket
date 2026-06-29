using System;
using System.Reflection;
using System.Runtime.InteropServices;
using Hps.Transport;
using Xunit;

namespace Hps.Transport.IoUring.Tests
{
    public sealed class IoUringNativeShapeTests
    {
        // native syscall adapter 는 transport 에서 raw P/Invoke 를 직접 만지지 않게 하는 첫 경계다.
        // reflection 으로 시작해 production type 부재를 compile failure 가 아니라 요구사항 failure 로 확인한다.
        [Fact]
        public void IoUringNative_TypeExists()
        {
            Type? type = Type.GetType("Hps.Transport.IoUringNative, Hps.Transport.IoUring");

            Assert.NotNull(type);
        }

        // non-Linux 에서는 syscall 번호나 mmap wrapper 로 들어가면 안 된다.
        // capability probe 와 transport unsupported boundary 가 같은 platform 판단을 공유해야 한다.
        [Fact]
        public void GetPlatformStatus_WhenNotLinux_ReturnsUnsupportedOperatingSystem()
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
                return;

            Type? type = Type.GetType("Hps.Transport.IoUringNative, Hps.Transport.IoUring");
            Assert.NotNull(type);

            MethodInfo? method = type!.GetMethod("GetPlatformStatus", BindingFlags.Static | BindingFlags.NonPublic);
            Assert.NotNull(method);

            object? status = method!.Invoke(null, null);

            Assert.Equal(IoUringCapabilityStatus.UnsupportedOperatingSystem, status);
        }

        // unsupported platform 은 명시적 NotSupportedException 으로 드러나야 한다.
        // 그래야 host selector 나 explicit backend 선택자가 fallback/error 를 구분할 수 있다.
        [Fact]
        public void ThrowIfUnsupportedPlatform_WhenNotLinux_ThrowsNotSupportedException()
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
                return;

            Type? type = Type.GetType("Hps.Transport.IoUringNative, Hps.Transport.IoUring");
            Assert.NotNull(type);

            MethodInfo? method = type!.GetMethod("ThrowIfUnsupportedPlatform", BindingFlags.Static | BindingFlags.NonPublic);
            Assert.NotNull(method);

            TargetInvocationException exception = Assert.Throws<TargetInvocationException>(delegate()
            {
                method!.Invoke(null, null);
            });

            Assert.IsType<NotSupportedException>(exception.InnerException);
        }
    }
}
