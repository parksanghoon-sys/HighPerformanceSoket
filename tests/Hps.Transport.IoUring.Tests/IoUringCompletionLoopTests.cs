using System;
using System.Reflection;
using System.Threading.Tasks;
using Xunit;

namespace Hps.Transport.IoUring.Tests
{
    public sealed class IoUringCompletionLoopTests
    {
        // completion loop는 native CQE drain과 managed context 완료 사이의 단일 routing 경계다.
        // 타입과 entry point를 먼저 assertion failure로 고정해, 이후 Linux pump 구현이 임의 경로로 흩어지지 않게 한다.
        [Fact]
        public void CompletionLoop_WhenInspected_ExposesDispatchBoundary()
        {
            Type? loopType = Type.GetType("Hps.Transport.IoUringCompletionLoop, Hps.Transport.IoUring");
            Assert.NotNull(loopType);

            Assert.True(typeof(IDisposable).IsAssignableFrom(loopType!));
            Assert.NotNull(loopType!.GetMethod("CreateForTests", BindingFlags.Static | BindingFlags.NonPublic));
            Assert.NotNull(loopType.GetMethod("DispatchCompletion", BindingFlags.Instance | BindingFlags.NonPublic));
            Assert.NotNull(loopType.GetMethod("StartAsync", BindingFlags.Instance | BindingFlags.NonPublic));
            Assert.NotNull(loopType.GetMethod("StopAsync", BindingFlags.Instance | BindingFlags.NonPublic));
        }

        // CQE user_data token이 등록된 context와 일치하면 해당 context의 waiter만 완료되어야 한다.
        // native syscall 없이 pure dispatch를 먼저 고정해 Linux 전용 drain loop를 붙이기 전 routing bug를 좁힌다.
        [Fact]
        public async Task DispatchCompletion_WhenTokenMatches_CompletesRegisteredContext()
        {
            IoUringOperationRegistry registry = new IoUringOperationRegistry();
            IoUringOperationContext context = registry.Register(IoUringOperationKind.Receive);
            object loop = CreateLoopForTests(registry);

            ValueTask<IoUringCompletion> wait = context.WaitAsync();
            InvokeDispatch(loop, new IoUringCompletion(context.Token, 12, 0));

            IoUringCompletion completion = await wait;

            Assert.Equal(context.Token, completion.Token);
            Assert.Equal(12, completion.Result);
        }

        // 알 수 없는 token을 조용히 무시하면 native completion 누락이 운영 중에 숨어버린다.
        // 아직 close 정책을 붙이지 않은 단계에서는 InvalidOperationException으로 mapping bug를 즉시 드러낸다.
        [Fact]
        public void DispatchCompletion_WhenTokenMissing_ThrowsInvalidOperationException()
        {
            IoUringOperationRegistry registry = new IoUringOperationRegistry();
            object loop = CreateLoopForTests(registry);

            TargetInvocationException exception = Assert.Throws<TargetInvocationException>(delegate()
            {
                InvokeDispatch(loop, new IoUringCompletion(1000, -1, 0));
            });

            Assert.IsType<InvalidOperationException>(exception.InnerException);
        }

        // shutdown 이 시작된 뒤에는 socket close 로 인해 이미 unregister 된 operation 의 CQE가 늦게 도착할 수 있다.
        // 이 stale completion 은 새 context 로 라우팅하면 안 되지만, transport stop 을 실패시키는 fatal 오류도 아니므로 조용히 버린다.
        [Fact]
        public void DispatchCompletion_WhenShutdownStartedAndTokenWasUnregistered_IgnoresStaleCompletion()
        {
            IoUringOperationRegistry registry = new IoUringOperationRegistry();
            IoUringOperationContext context = registry.Register(IoUringOperationKind.Receive);
            object loop = CreateLoopForTests(registry);

            Assert.True(registry.Unregister(context.Token));
            InvokeBeginShutdown(loop);

            InvokeDispatch(loop, new IoUringCompletion(context.Token, -125, 0));
        }

        // context가 아직 WaitAsync를 호출하지 않았다면 completion을 받을 준비가 된 operation이 아니다.
        // 이 상태를 허용하면 submit 전 context나 이미 회수된 context가 완료된 것처럼 보일 수 있다.
        [Fact]
        public void DispatchCompletion_WhenContextHasNoWaiter_ThrowsInvalidOperationException()
        {
            IoUringOperationRegistry registry = new IoUringOperationRegistry();
            IoUringOperationContext context = registry.Register(IoUringOperationKind.Receive);
            object loop = CreateLoopForTests(registry);

            TargetInvocationException exception = Assert.Throws<TargetInvocationException>(delegate()
            {
                InvokeDispatch(loop, new IoUringCompletion(context.Token, 1, 0));
            });

            Assert.IsType<InvalidOperationException>(exception.InnerException);
        }

        private static object CreateLoopForTests(IoUringOperationRegistry registry)
        {
            Type? loopType = Type.GetType("Hps.Transport.IoUringCompletionLoop, Hps.Transport.IoUring");
            Assert.NotNull(loopType);

            MethodInfo? method = loopType!.GetMethod("CreateForTests", BindingFlags.Static | BindingFlags.NonPublic);
            Assert.NotNull(method);

            object? result = method!.Invoke(null, new object[] { registry });
            Assert.NotNull(result);
            return result!;
        }

        private static void InvokeDispatch(object loop, IoUringCompletion completion)
        {
            MethodInfo? method = loop.GetType().GetMethod(
                "DispatchCompletion",
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.NotNull(method);

            method!.Invoke(loop, new object[] { completion });
        }

        private static void InvokeBeginShutdown(object loop)
        {
            MethodInfo? method = loop.GetType().GetMethod(
                "BeginShutdown",
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.NotNull(method);

            method!.Invoke(loop, Array.Empty<object>());
        }
    }
}
