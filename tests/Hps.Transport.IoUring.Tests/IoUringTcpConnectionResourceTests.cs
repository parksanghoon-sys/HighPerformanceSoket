using System;
using System.Reflection;
using Xunit;

namespace Hps.Transport.IoUring.Tests
{
    public sealed class IoUringTcpConnectionResourceTests
    {
        [Fact]
        public void ResourceContract_WhenInspected_OwnsFixedSendRegistryInternally()
        {
            // fixed send registry를 transport public surface로 노출하지 않고,
            // TCP connection resource의 내부 lifetime owner로만 붙이는 계약을 고정한다.
            Type resourceType = typeof(IoUringQueue).Assembly.GetType("Hps.Transport.IoUringTcpConnectionResource")!;

            PropertyInfo? property = resourceType.GetProperty(
                "FixedSendBufferRegistry",
                BindingFlags.Instance | BindingFlags.NonPublic);
            MethodInfo? testSeam = resourceType.GetMethod(
                "SetFixedSendBufferRegistryForTests",
                BindingFlags.Instance | BindingFlags.NonPublic);

            Assert.NotNull(property);
            Assert.NotNull(testSeam);
        }
    }
}
