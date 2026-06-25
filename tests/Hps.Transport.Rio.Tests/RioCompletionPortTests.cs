using System;
using System.Reflection;
using System.Threading.Tasks;
using Xunit;

namespace Hps.Transport.Rio.Tests
{
    public sealed class RioCompletionPortTests
    {
        // IOCP pump wiring 전에 CQ별 signal owner 의 가장 작은 계약을 고정한다.
        // pump 가 signal 을 깨우면 대기 중인 waiter 가 timer 없이 완료되어야 한다.
        [Fact]
        public async Task Signal_WhenCompleted_WakesSingleWaiter()
        {
            Type portType = GetRequiredType("Hps.Transport.RioCompletionPort");
            using (IDisposable port = (IDisposable)InvokeStatic(portType, "CreateForTests"))
            using (IDisposable signal = (IDisposable)InvokeInstance(port, "CreateSignalForTests"))
            {
                Task wait = (Task)InvokeInstance(signal, "WaitAsync");

                InvokeInstance(signal, "CompleteForTests");

                Task completed = await Task.WhenAny(wait, Task.Delay(TimeSpan.FromSeconds(1)));
                Assert.Same(wait, completed);
                await wait;
            }
        }

        // connection/resource close 가 notification wait 보다 먼저 오면 waiter 를 남겨서는 안 된다.
        // dispose 는 후속 RIO close path 에서 ObjectDisposedException 으로 pump 를 종료시키는 wake 신호다.
        [Fact]
        public async Task Signal_WhenDisposed_WakesWaiterAsDisposed()
        {
            Type portType = GetRequiredType("Hps.Transport.RioCompletionPort");
            using (IDisposable port = (IDisposable)InvokeStatic(portType, "CreateForTests"))
            {
                IDisposable signal = (IDisposable)InvokeInstance(port, "CreateSignalForTests");
                Task wait = (Task)InvokeInstance(signal, "WaitAsync");

                signal.Dispose();

                await Assert.ThrowsAsync<ObjectDisposedException>(async delegate()
                {
                    await wait;
                });
            }
        }

        private static Type GetRequiredType(string fullName)
        {
            Type? type = typeof(RioTransport).Assembly.GetType(fullName);
            Assert.NotNull(type);
            return type!;
        }

        private static object InvokeStatic(Type type, string name)
        {
            MethodInfo? method = type.GetMethod(name, BindingFlags.Static | BindingFlags.NonPublic);
            Assert.NotNull(method);
            return method!.Invoke(null, Array.Empty<object>())!;
        }

        private static object InvokeInstance(object instance, string name)
        {
            MethodInfo? method = instance.GetType().GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.NotNull(method);
            return method!.Invoke(instance, Array.Empty<object>())!;
        }
    }
}
