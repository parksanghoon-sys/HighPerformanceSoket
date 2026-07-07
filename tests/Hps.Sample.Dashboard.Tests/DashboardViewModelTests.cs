using System;
using System.Collections;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using System.Windows.Input;
using Xunit;

namespace Hps.Sample.Dashboard.Tests
{
    public sealed class DashboardViewModelTests
    {
        [Fact]
        public void Constructor_WhenCreated_ExposesInitialDashboardState()
        {
            // UI 첫 렌더링 전에 service 실행 없이도 안정적인 초기 상태와 command binding이 준비되어야 한다.
            Type viewModelType = RequireType("Hps.Sample.Dashboard.ViewModels.DashboardViewModel");
            object viewModel = Activator.CreateInstance(viewModelType)!;

            Assert.Equal("Stopped", ReadProperty(viewModel, "ServerStatus")!.ToString());
            Assert.Equal("중지됨", ReadProperty(viewModel, "ServerStatusText"));
            Assert.True(((ICommand)ReadProperty(viewModel, "StartServerCommand")!).CanExecute(null));
            Assert.False(((ICommand)ReadProperty(viewModel, "StopServerCommand")!).CanExecute(null));
            Assert.True(((ICommand)ReadProperty(viewModel, "RunTcpSmokeCommand")!).CanExecute(null));
            Assert.True(((ICommand)ReadProperty(viewModel, "RunUdpSmokeCommand")!).CanExecute(null));
        }

        [Fact]
        public void AddLog_WhenMoreThanMaximumEntries_RemovesOldestEntries()
        {
            // sample dashboard는 장시간 켜둘 수 있으므로 log collection이 무한 증가하지 않아야 한다.
            Type viewModelType = RequireType("Hps.Sample.Dashboard.ViewModels.DashboardViewModel");
            object viewModel = Activator.CreateInstance(viewModelType, new object[] { 3 })!;

            Invoke(viewModel, "AddLog", "one");
            Invoke(viewModel, "AddLog", "two");
            Invoke(viewModel, "AddLog", "three");
            Invoke(viewModel, "AddLog", "four");

            IEnumerable entries = (IEnumerable)ReadProperty(viewModel, "LogEntries")!;
            Assert.Equal(new[] { "two", "three", "four" }, entries.Cast<string>().ToArray());
        }

        [Fact]
        public void ApplySmokeResult_WhenResultContainsCounters_UpdatesSummaryText()
        {
            // TCP/UDP smoke 결과는 UI에서 sent/received/drop/error/leak를 한 줄로 비교할 수 있어야 한다.
            Type viewModelType = RequireType("Hps.Sample.Dashboard.ViewModels.DashboardViewModel");
            Type resultType = RequireType("Hps.Sample.Dashboard.Models.SmokeRunResult");
            object viewModel = Activator.CreateInstance(viewModelType)!;
            object result = Activator.CreateInstance(resultType, new object[] { "TCP", true, 1, 1, 0L, 0, 0, "ok" })!;

            Invoke(viewModel, "ApplySmokeResult", result);

            Assert.Equal(
                "TCP: sent=1, received=1, dropped=0, payload-errors=0, pool-rented=0",
                ReadProperty(viewModel, "LastSmokeSummary"));
        }

        [Fact]
        public async Task AsyncCommand_WhenRunning_DisablesConcurrentExecution()
        {
            // 사용자가 smoke 버튼을 연타해도 같은 작업이 동시에 중복 실행되면 안 된다.
            Type commandType = RequireType("Hps.Sample.Dashboard.Commands.AsyncRelayCommand");
            int executionCount = 0;
            Func<Task> action = async delegate
            {
                executionCount++;
                await Task.Delay(50);
            };

            object command = Activator.CreateInstance(commandType, new object[] { action })!;
            MethodInfo executeAsync = commandType.GetMethod("ExecuteAsync", Type.EmptyTypes)!;

            Task first = (Task)executeAsync.Invoke(command, Array.Empty<object>())!;
            Assert.False(((ICommand)command).CanExecute(null));

            ((ICommand)command).Execute(null);
            await first;

            Assert.Equal(1, executionCount);
            Assert.True(((ICommand)command).CanExecute(null));
        }

        [Fact]
        public async Task RunTcpSmokeCommand_WhenExecuted_AddsResultToLog()
        {
            // UI button은 service 결과를 log와 summary에 반영해야 사용자가 성공/실패를 즉시 판단할 수 있다.
            Type viewModelType = RequireType("Hps.Sample.Dashboard.ViewModels.DashboardViewModel");
            Type resultType = RequireType("Hps.Sample.Dashboard.Models.SmokeRunResult");
            MethodInfo? createForTests = viewModelType.GetMethod("CreateForTests", BindingFlags.Public | BindingFlags.Static);
            Assert.NotNull(createForTests);

            Func<Task<object>> tcpSmoke = delegate
            {
                object result = Activator.CreateInstance(resultType, new object[] { "TCP", true, 1, 1, 0L, 0, 0, "ok" })!;
                return Task.FromResult(result);
            };
            Func<Task<object>> udpSmoke = delegate
            {
                object result = Activator.CreateInstance(resultType, new object[] { "UDP", true, 0, 0, 0L, 0, 0, "not-run" })!;
                return Task.FromResult(result);
            };

            object viewModel = createForTests!.Invoke(null, new object[] { tcpSmoke, udpSmoke })!;
            object command = ReadProperty(viewModel, "RunTcpSmokeCommand")!;
            MethodInfo executeAsync = command.GetType().GetMethod("ExecuteAsync", Type.EmptyTypes)!;

            Task task = (Task)executeAsync.Invoke(command, Array.Empty<object>())!;
            await task;

            IEnumerable entries = (IEnumerable)ReadProperty(viewModel, "LogEntries")!;
            Assert.Equal(
                "TCP: sent=1, received=1, dropped=0, payload-errors=0, pool-rented=0",
                ReadProperty(viewModel, "LastSmokeSummary"));
            Assert.Contains(entries.Cast<string>(), entry => entry.Contains("TCP smoke 성공"));
        }

        [Fact]
        public async Task SmokeCommands_WhenExecuted_UpdateProtocolSpecificSummaries()
        {
            // TCP와 UDP 상태 카드는 서로 다른 의미를 가지므로 마지막 smoke 결과 하나를 공유하면
            // 사용자가 어떤 protocol이 성공했는지 잘못 판단할 수 있다.
            Type viewModelType = RequireType("Hps.Sample.Dashboard.ViewModels.DashboardViewModel");
            Type resultType = RequireType("Hps.Sample.Dashboard.Models.SmokeRunResult");
            MethodInfo? createForTests = viewModelType.GetMethod("CreateForTests", BindingFlags.Public | BindingFlags.Static);
            Assert.NotNull(createForTests);

            Func<Task<object>> tcpSmoke = delegate
            {
                object result = Activator.CreateInstance(resultType, new object[] { "TCP", true, 1, 1, 0L, 0, 0, "tcp-ok" })!;
                return Task.FromResult(result);
            };
            Func<Task<object>> udpSmoke = delegate
            {
                object result = Activator.CreateInstance(resultType, new object[] { "UDP", true, 1, 1, 0L, 0, 0, "udp-ok" })!;
                return Task.FromResult(result);
            };

            object viewModel = createForTests!.Invoke(null, new object[] { tcpSmoke, udpSmoke })!;
            object tcpCommand = ReadProperty(viewModel, "RunTcpSmokeCommand")!;
            object udpCommand = ReadProperty(viewModel, "RunUdpSmokeCommand")!;
            MethodInfo tcpExecuteAsync = tcpCommand.GetType().GetMethod("ExecuteAsync", Type.EmptyTypes)!;
            MethodInfo udpExecuteAsync = udpCommand.GetType().GetMethod("ExecuteAsync", Type.EmptyTypes)!;

            await (Task)tcpExecuteAsync.Invoke(tcpCommand, Array.Empty<object>())!;

            Assert.Equal(
                "TCP: sent=1, received=1, dropped=0, payload-errors=0, pool-rented=0",
                ReadProperty(viewModel, "TcpSmokeSummary"));
            Assert.Equal(string.Empty, ReadProperty(viewModel, "UdpSmokeSummary"));

            await (Task)udpExecuteAsync.Invoke(udpCommand, Array.Empty<object>())!;

            Assert.Equal(
                "TCP: sent=1, received=1, dropped=0, payload-errors=0, pool-rented=0",
                ReadProperty(viewModel, "TcpSmokeSummary"));
            Assert.Equal(
                "UDP: sent=1, received=1, dropped=0, payload-errors=0, pool-rented=0",
                ReadProperty(viewModel, "UdpSmokeSummary"));
        }

        private static Type RequireType(string fullName)
        {
            Type? type = Type.GetType(fullName + ", Hps.Sample.Dashboard");
            Assert.NotNull(type);
            return type!;
        }

        private static object? ReadProperty(object target, string propertyName)
        {
            PropertyInfo? property = target.GetType().GetProperty(propertyName);
            Assert.NotNull(property);
            return property!.GetValue(target);
        }

        private static void Invoke(object target, string methodName, params object[] args)
        {
            MethodInfo? method = target.GetType().GetMethod(methodName);
            Assert.NotNull(method);
            method!.Invoke(target, args);
        }
    }
}
