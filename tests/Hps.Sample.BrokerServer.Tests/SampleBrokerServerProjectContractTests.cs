using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using Xunit;

namespace Hps.Sample.BrokerServer.Tests
{
    public sealed class SampleBrokerServerProjectContractTests
    {
        // selector가 io_uring public capability와 transport를 직접 사용하므로 sample project가 backend assembly를 명시 참조해야 한다.
        // 이 참조가 없으면 Windows에서만 우연히 다른 경로가 통과해도 Linux sample composition build가 깨질 수 있다.
        [Fact]
        public void BrokerSampleProject_WhenInspected_ReferencesIoUringBackend()
        {
            string projectPath = Path.Combine(
                FindRepositoryRoot(),
                "samples",
                "Hps.Sample.BrokerServer",
                "Hps.Sample.BrokerServer.csproj");
            XDocument document = XDocument.Load(projectPath);
            IEnumerable<string> references = document
                .Descendants("ProjectReference")
                .Select(element => ((string?)element.Attribute("Include") ?? string.Empty).Replace('\\', '/'));

            Assert.Contains("../../src/Hps.Transport.IoUring/Hps.Transport.IoUring.csproj", references);
        }

        private static string FindRepositoryRoot()
        {
            string? current = AppContext.BaseDirectory;
            while (current != null)
            {
                if (File.Exists(Path.Combine(current, "HighPerformanceSocket.slnx")))
                    return current;

                current = Directory.GetParent(current)?.FullName;
            }

            throw new InvalidOperationException("HighPerformanceSocket.slnx 파일을 찾을 수 없습니다.");
        }
    }
}
