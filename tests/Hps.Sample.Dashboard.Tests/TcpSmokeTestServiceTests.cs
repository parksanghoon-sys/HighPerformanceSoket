using System;
using System.Reflection;
using System.Threading.Tasks;
using Xunit;

namespace Hps.Sample.Dashboard.Tests
{
    public sealed class TcpSmokeTestServiceTests
    {
        [Fact]
        public async Task RunAsync_WhenBrokerLoopbackRuns_DeliversPayloadWithoutLeak()
        {
            // 실제 SAEA TCP listener/receive/send pump와 Broker fan-out을 묶어 샘플 버튼이 의미 있는 end-to-end 검증이 되게 한다.
            Type serviceType = RequireType("Hps.Sample.Dashboard.Services.TcpSmokeTestService");
            object service = Activator.CreateInstance(serviceType)!;
            MethodInfo runAsync = serviceType.GetMethod("RunAsync", Type.EmptyTypes)!;

            object result = await InvokeRunAsync(runAsync, service);

            Assert.True((bool)ReadProperty(result, "Succeeded")!, (string)ReadProperty(result, "Message")!);
            Assert.Equal("TCP", ReadProperty(result, "Protocol"));
            Assert.Equal(1, ReadProperty(result, "Sent"));
            Assert.Equal(1, ReadProperty(result, "Received"));
            Assert.Equal(0L, ReadProperty(result, "Dropped"));
            Assert.Equal(0, ReadProperty(result, "PayloadErrors"));
            Assert.Equal(0, ReadProperty(result, "PoolRented"));
        }

        private static Type RequireType(string fullName)
        {
            Type? type = Type.GetType(fullName + ", Hps.Sample.Dashboard");
            Assert.NotNull(type);
            return type!;
        }

        private static async Task<object> InvokeRunAsync(MethodInfo runAsync, object service)
        {
            object? taskObject = runAsync.Invoke(service, Array.Empty<object>());
            Assert.NotNull(taskObject);

            Task task = (Task)taskObject!;
            await task.ConfigureAwait(false);

            PropertyInfo? resultProperty = task.GetType().GetProperty("Result");
            Assert.NotNull(resultProperty);
            return resultProperty!.GetValue(task)!;
        }

        private static object? ReadProperty(object target, string propertyName)
        {
            PropertyInfo? property = target.GetType().GetProperty(propertyName);
            Assert.NotNull(property);
            return property!.GetValue(target);
        }
    }
}
