using System;
using System.Reflection;
using Xunit;

namespace Hps.Transport.IoUring.Tests
{
    public sealed class IoUringOperationRegistryTests
    {
        // SQE user_data는 native CQE에서 돌아오는 유일한 managed routing key다.
        // 이 타입 경계가 없으면 completion loop가 어떤 connection/context를 깨워야 하는지 안전하게 알 수 없다.
        [Fact]
        public void OperationRegistryTypes_WhenInspected_Exist()
        {
            Assert.NotNull(Type.GetType("Hps.Transport.IoUringOperationKind, Hps.Transport.IoUring"));
            Assert.NotNull(Type.GetType("Hps.Transport.IoUringCompletion, Hps.Transport.IoUring"));
            Assert.NotNull(Type.GetType("Hps.Transport.IoUringOperationContext, Hps.Transport.IoUring"));
            Assert.NotNull(Type.GetType("Hps.Transport.IoUringOperationRegistry, Hps.Transport.IoUring"));
        }

        // registry는 token 발급, token->context resolve, 제거 경계를 모두 제공해야 한다.
        // reflection Red를 먼저 두면 아직 production 타입이 없을 때도 컴파일 실패가 아니라 assertion failure로 시작할 수 있다.
        [Fact]
        public void OperationRegistry_WhenInspected_ExposesRequiredMethods()
        {
            Type? registryType = Type.GetType("Hps.Transport.IoUringOperationRegistry, Hps.Transport.IoUring");
            Assert.NotNull(registryType);

            Assert.NotNull(registryType!.GetMethod("Register", BindingFlags.Instance | BindingFlags.NonPublic));
            Assert.NotNull(registryType.GetMethod("Resolve", BindingFlags.Instance | BindingFlags.NonPublic));
            Assert.NotNull(registryType.GetMethod("TryResolve", BindingFlags.Instance | BindingFlags.NonPublic));
            Assert.NotNull(registryType.GetMethod("Unregister", BindingFlags.Instance | BindingFlags.NonPublic));
        }

        // token은 한 번 발급되면 같은 context로만 resolve되어야 하며, unregister 뒤에는 다시 resolve되면 안 된다.
        // CQE user_data 재사용이나 stale token 처리가 틀리면 다른 연결의 작업 완료로 오인될 수 있어 이 경계를 고정한다.
        [Fact]
        public void OperationRegistry_WhenTokenRegistered_RoutesAndRemovesContext()
        {
            Type registryType = RequiredType("Hps.Transport.IoUringOperationRegistry, Hps.Transport.IoUring");
            object registry = CreateInstance(registryType);
            object receiveKind = Enum.Parse(RequiredType("Hps.Transport.IoUringOperationKind, Hps.Transport.IoUring"), "Receive");

            object context = InvokeRequired(registry, "Register", receiveKind);
            ulong token = ReadToken(context);

            object resolved = InvokeRequired(registry, "Resolve", token);
            object?[] tryResolveArguments = new object?[] { token, null };
            object tryResolveResult = InvokeRequired(registry, "TryResolve", tryResolveArguments);
            object unregisterResult = InvokeRequired(registry, "Unregister", token);
            object?[] missingArguments = new object?[] { token, null };
            object missingResult = InvokeRequired(registry, "TryResolve", missingArguments);
            TargetInvocationException exception = Assert.Throws<TargetInvocationException>(delegate()
            {
                InvokeRequired(registry, "Resolve", token);
            });

            Assert.True(token > 0);
            Assert.Same(context, resolved);
            Assert.True((bool)tryResolveResult);
            Assert.Same(context, tryResolveArguments[1]);
            Assert.True((bool)unregisterResult);
            Assert.False((bool)missingResult);
            Assert.Null(missingArguments[1]);
            Assert.IsType<InvalidOperationException>(exception.InnerException);
        }

        // operation context는 waiter가 등록된 작업만 completion으로 끝낼 수 있어야 한다.
        // wait 없이 completion을 허용하면 submit되지 않았거나 이미 회수된 context가 완료된 것처럼 보일 수 있다.
        [Fact]
        public void OperationContext_WhenCompletedWithoutWait_ThrowsInvalidOperationException()
        {
            object context = CreateRegisteredContext("Receive");
            object completion = CreateCompletion(ReadToken(context), 1, 0);

            TargetInvocationException exception = Assert.Throws<TargetInvocationException>(delegate()
            {
                InvokeVoid(context, "Complete", completion);
            });

            Assert.IsType<InvalidOperationException>(exception.InnerException);
        }

        // 정상 경로는 WaitAsync 이후 Complete가 정확히 한 번 결과를 전달하는 것이다.
        // 두 번째 Complete는 같은 CQE를 중복 처리하는 소유권 오류이므로 즉시 실패해야 한다.
        [Fact]
        public void OperationContext_WhenCompletedAfterWait_ReturnsCompletionOnce()
        {
            object context = CreateRegisteredContext("Receive");
            ulong token = ReadToken(context);
            object wait = InvokeRequired(context, "WaitAsync");
            object completion = CreateCompletion(token, 12, 3);

            Assert.False((bool)ReadProperty(wait, "IsCompleted"));

            InvokeVoid(context, "Complete", completion);

            object result = GetValueTaskResult(wait);
            TargetInvocationException exception = Assert.Throws<TargetInvocationException>(delegate()
            {
                InvokeVoid(context, "Complete", completion);
            });

            Assert.Equal(token, (ulong)ReadProperty(result, "Token"));
            Assert.Equal(12, (int)ReadProperty(result, "Result"));
            Assert.Equal(3U, (uint)ReadProperty(result, "Flags"));
            Assert.IsType<InvalidOperationException>(exception.InnerException);
        }

        // 같은 context를 다음 SQE에 재사용할 때는 token/kind/waiter 상태가 모두 새 작업 기준으로 초기화되어야 한다.
        // 이전 작업의 completion state가 남으면 다음 wait가 즉시 완료되거나 double-complete로 오판될 수 있다.
        [Fact]
        public void OperationContext_WhenReset_PreparesNextOperation()
        {
            object context = CreateRegisteredContext("Receive");
            object firstWait = InvokeRequired(context, "WaitAsync");
            InvokeVoid(context, "Complete", CreateCompletion(ReadToken(context), 1, 0));
            GetValueTaskResult(firstWait);

            object sendKind = Enum.Parse(RequiredType("Hps.Transport.IoUringOperationKind, Hps.Transport.IoUring"), "Send");
            InvokeVoid(context, "Reset", 100UL, sendKind);

            object secondWait = InvokeRequired(context, "WaitAsync");
            InvokeVoid(context, "Complete", CreateCompletion(100UL, 2, 4));
            object secondResult = GetValueTaskResult(secondWait);

            Assert.Equal(100UL, ReadToken(context));
            Assert.Equal(100UL, (ulong)ReadProperty(secondResult, "Token"));
            Assert.Equal(2, (int)ReadProperty(secondResult, "Result"));
            Assert.Equal(4U, (uint)ReadProperty(secondResult, "Flags"));
        }

        private static object CreateRegisteredContext(string kindName)
        {
            Type registryType = RequiredType("Hps.Transport.IoUringOperationRegistry, Hps.Transport.IoUring");
            object registry = CreateInstance(registryType);
            object kind = Enum.Parse(RequiredType("Hps.Transport.IoUringOperationKind, Hps.Transport.IoUring"), kindName);

            return InvokeRequired(registry, "Register", kind);
        }

        private static object CreateCompletion(ulong token, int result, uint flags)
        {
            Type completionType = RequiredType("Hps.Transport.IoUringCompletion, Hps.Transport.IoUring");
            return CreateInstance(completionType, token, result, flags);
        }

        private static ulong ReadToken(object context)
        {
            return (ulong)ReadProperty(context, "Token");
        }

        private static object GetValueTaskResult(object wait)
        {
            object awaiter = InvokeMethod(wait, "GetAwaiter");
            return InvokeMethod(awaiter, "GetResult");
        }

        private static object InvokeRequired(object target, string methodName, params object?[] arguments)
        {
            return InvokeMethod(target, methodName, arguments);
        }

        private static void InvokeVoid(object target, string methodName, params object?[] arguments)
        {
            MethodInfo? method = target.GetType().GetMethod(
                methodName,
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            Assert.NotNull(method);

            method!.Invoke(target, arguments);
        }

        private static object InvokeMethod(object target, string methodName, params object?[] arguments)
        {
            MethodInfo? method = target.GetType().GetMethod(
                methodName,
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            Assert.NotNull(method);

            object? result = method!.Invoke(target, arguments);
            Assert.NotNull(result);
            return result!;
        }

        private static object ReadProperty(object target, string propertyName)
        {
            PropertyInfo? property = target.GetType().GetProperty(
                propertyName,
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            Assert.NotNull(property);

            object? result = property!.GetValue(target);
            Assert.NotNull(result);
            return result!;
        }

        private static Type RequiredType(string name)
        {
            Type? type = Type.GetType(name);
            Assert.NotNull(type);
            return type!;
        }

        private static object CreateInstance(Type type, params object[] arguments)
        {
            object? instance = Activator.CreateInstance(
                type,
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                null,
                arguments,
                null);
            Assert.NotNull(instance);
            return instance!;
        }
    }
}
