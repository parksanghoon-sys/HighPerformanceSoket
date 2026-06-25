using Hps.Buffers;
using Xunit;

namespace Hps.Transport.Rio.Tests
{
    public sealed class RioRegisteredBufferPoolTests
    {
        // RIO는 completion dequeue 전까지 registered buffer association이 살아 있어야 한다.
        // 이 테스트는 request 완료 신호가 들어오기 전 Dispose가 pool block을 반납하지 않게 고정한다.
        [Fact]
        public void Dispose_WhenRequestIsOutstanding_DoesNotReturnBlockUntilCompletion()
        {
            RioRegisteredBufferPool pool = new RioRegisteredBufferPool(64);
            RefCountedBuffer buffer = pool.RentReceiveBlock();

            Assert.Equal(1, pool.RentedCount);

            pool.Dispose();
            Assert.Equal(1, pool.RentedCount);

            pool.CompleteRequest(buffer);
            Assert.Equal(0, pool.RentedCount);
        }

        // 완료가 먼저 오고 Dispose가 나중에 오면 block은 정확히 한 번만 반납되어야 한다.
        // double completion이 들어와도 RefCountedBuffer Release가 두 번 호출되면 안 된다.
        [Fact]
        public void CompleteRequest_WhenCalledTwice_ReleasesOnlyOnce()
        {
            RioRegisteredBufferPool pool = new RioRegisteredBufferPool(64);
            RefCountedBuffer buffer = pool.RentReceiveBlock();

            pool.CompleteRequest(buffer);
            pool.CompleteRequest(buffer);

            Assert.Equal(0, pool.RentedCount);
            pool.Dispose();
        }
    }
}
