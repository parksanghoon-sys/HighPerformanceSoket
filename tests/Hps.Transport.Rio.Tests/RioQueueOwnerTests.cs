using Xunit;

namespace Hps.Transport.Rio.Tests
{
    public sealed class RioQueueOwnerTests
    {
        // RIO request queue는 MaxOutstandingReceive 한도를 넘기면 안 된다.
        // native call 전에 owner가 quota를 막아야 completion queue capacity 초과를 피할 수 있다.
        [Fact]
        public void TryReserveReceive_WhenLimitReached_ReturnsFalseUntilCompletion()
        {
            RioRequestQueue queue = new RioRequestQueue(1, 1);

            Assert.True(queue.TryReserveReceive());
            Assert.False(queue.TryReserveReceive());

            queue.CompleteReceive();
            Assert.True(queue.TryReserveReceive());
        }

        // send quota도 receive와 독립적으로 관리해야 한다.
        // fan-out send pump가 quota 초과 상태에서 같은 request queue로 추가 posting하지 않게 만든다.
        [Fact]
        public void TryReserveSend_WhenLimitReached_ReturnsFalseUntilCompletion()
        {
            RioRequestQueue queue = new RioRequestQueue(1, 1);

            Assert.True(queue.TryReserveSend());
            Assert.False(queue.TryReserveSend());

            queue.CompleteSend();
            Assert.True(queue.TryReserveSend());
        }
    }
}
