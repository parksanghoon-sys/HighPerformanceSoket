using System;
using System.Collections;
using System.Linq;
using System.Reflection;
using Hps.Transport;
using Xunit;

namespace Hps.Sample.Dashboard.Tests
{
    public sealed class DiagnosticsSnapshotServiceTests
    {
        [Fact]
        public void CreateRows_WhenTransportHasAggregateSnapshot_ReturnsTcpAndUdpRows()
        {
            // BrokerServer는 diagnostics API를 직접 노출하지 않으므로 service는 공유 transport 참조에서 snapshot을 읽어야 한다.
            FakeDiagnosticsTransport transport = new FakeDiagnosticsTransport(
                new TransportDiagnosticsSnapshot(
                    tcpDroppedPendingSendCount: 2,
                    udpDroppedPendingSendCount: 3,
                    tcpPendingSendQueueHighWatermark: 4,
                    udpPendingSendQueueHighWatermark: 5));
            Type serviceType = RequireType("Hps.Sample.Dashboard.Services.DiagnosticsSnapshotService");
            object service = Activator.CreateInstance(serviceType)!;

            IEnumerable rows = (IEnumerable)Invoke(service, "CreateRows", transport)!;
            object[] materialized = rows.Cast<object>().ToArray();

            Assert.Equal(2, materialized.Length);
            Assert.Equal("TCP", ReadProperty(materialized[0], "Name"));
            Assert.Equal(2L, ReadProperty(materialized[0], "DroppedPendingSendCount"));
            Assert.Equal(4L, ReadProperty(materialized[0], "PendingSendQueueHighWatermark"));
            Assert.Equal("UDP", ReadProperty(materialized[1], "Name"));
            Assert.Equal(3L, ReadProperty(materialized[1], "DroppedPendingSendCount"));
            Assert.Equal(5L, ReadProperty(materialized[1], "PendingSendQueueHighWatermark"));
        }

        [Fact]
        public void DashboardBrokerService_WhenInspected_ExposesDiagnosticsSource()
        {
            // UI diagnostics는 server가 아니라 service가 소유한 동일 transport 참조에서 읽어야 하므로 public 경계를 먼저 고정한다.
            Type serviceType = RequireType("Hps.Sample.Dashboard.Services.DashboardBrokerService");
            PropertyInfo? diagnosticsSource = serviceType.GetProperty("DiagnosticsSource");

            Assert.NotNull(diagnosticsSource);
            Assert.Equal(typeof(object), diagnosticsSource!.PropertyType);
        }

        private static Type RequireType(string fullName)
        {
            Type? type = Type.GetType(fullName + ", Hps.Sample.Dashboard");
            Assert.NotNull(type);
            return type!;
        }

        private static object? Invoke(object target, string methodName, params object[] args)
        {
            MethodInfo? method = target.GetType().GetMethod(methodName);
            Assert.NotNull(method);
            return method!.Invoke(target, args);
        }

        private static object? ReadProperty(object target, string propertyName)
        {
            PropertyInfo? property = target.GetType().GetProperty(propertyName);
            Assert.NotNull(property);
            return property!.GetValue(target);
        }

        private sealed class FakeDiagnosticsTransport : ITransportDiagnostics
        {
            private readonly TransportDiagnosticsSnapshot _snapshot;

            internal FakeDiagnosticsTransport(TransportDiagnosticsSnapshot snapshot)
            {
                _snapshot = snapshot;
            }

            public TransportDiagnosticsSnapshot GetDiagnosticsSnapshot()
            {
                return _snapshot;
            }
        }
    }
}
