using System;
using System.Reflection;
using System.Runtime.InteropServices;
using Xunit;

namespace Hps.Transport.IoUring.Tests
{
    public sealed class IoUringQueueTests
    {
        // queue owner 는 fd 와 mmap 수명을 transport 에서 분리하는 핵심 경계다.
        // 타입 부재를 먼저 assertion failure 로 고정한다.
        [Fact]
        public void IoUringQueue_TypeExists()
        {
            Type? type = Type.GetType("Hps.Transport.IoUringQueue, Hps.Transport.IoUring");

            Assert.NotNull(type);
        }

        // non-Linux 에서는 setup syscall 을 절대 호출하지 않아야 한다.
        // CreateForProbe 가 NotSupportedException 으로 수렴하면 Windows 개발 환경에서도 안전하게 테스트할 수 있다.
        [Fact]
        public void CreateForProbe_WhenNotLinux_ThrowsNotSupportedException()
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
                return;

            Type? type = Type.GetType("Hps.Transport.IoUringQueue, Hps.Transport.IoUring");
            Assert.NotNull(type);

            MethodInfo? method = type!.GetMethod("CreateForProbe", BindingFlags.Static | BindingFlags.NonPublic);
            Assert.NotNull(method);

            TargetInvocationException exception = Assert.Throws<TargetInvocationException>(delegate()
            {
                method!.Invoke(null, new object[] { 2U });
            });

            Assert.IsType<NotSupportedException>(exception.InnerException);
        }

        // Linux 에서 kernel/seccomp 가 허용하면 작은 ring 을 만들고 즉시 닫을 수 있어야 한다.
        // unavailable 환경은 capability probe 가 처리하므로 이 테스트는 exception escape 만 막는다.
        [Fact]
        public void CreateForProbe_WhenLinux_DoesNotEscapeUnexpectedException()
        {
            if (!RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
                return;

            Type? type = Type.GetType("Hps.Transport.IoUringQueue, Hps.Transport.IoUring");
            Assert.NotNull(type);

            MethodInfo? method = type!.GetMethod("TryCreateForProbe", BindingFlags.Static | BindingFlags.NonPublic);
            Assert.NotNull(method);

            object? result = method!.Invoke(null, new object[] { 2U });

            Assert.NotNull(result);
        }
    }
}
