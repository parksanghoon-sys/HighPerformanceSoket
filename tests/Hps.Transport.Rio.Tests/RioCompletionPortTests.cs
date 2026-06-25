using System;
using System.Threading.Tasks;
using Hps.Transport;
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
            using (RioCompletionPort port = RioCompletionPort.CreateForTests())
            using (RioCompletionSignal signal = port.CreateSignalForTests())
            {
                Task wait = signal.WaitAsync();

                signal.CompleteForTests();

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
            using (RioCompletionPort port = RioCompletionPort.CreateForTests())
            {
                RioCompletionSignal signal = port.CreateSignalForTests();
                Task wait = signal.WaitAsync();

                signal.Dispose();

                await Assert.ThrowsAsync<ObjectDisposedException>(async delegate()
                {
                    await wait;
                });
            }
        }
    }
}
