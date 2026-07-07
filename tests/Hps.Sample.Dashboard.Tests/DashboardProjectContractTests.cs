using System;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using Xunit;

namespace Hps.Sample.Dashboard.Tests
{
    public sealed class DashboardProjectContractTests
    {
        [Fact]
        public void DashboardProject_WhenInspected_UsesWpfWindowsBuildContract()
        {
            // WPF project는 루트 net9.0 기본값을 그대로 상속하면 빌드되지 않는다.
            // 이 테스트는 sample 구현 전에 Windows TFM, UseWPF, WinExe 계약을 먼저 고정한다.
            string projectPath = Path.GetFullPath(Path.Combine(
                AppContext.BaseDirectory,
                "..",
                "..",
                "..",
                "..",
                "..",
                "samples",
                "Hps.Sample.Dashboard",
                "Hps.Sample.Dashboard.csproj"));

            Assert.True(File.Exists(projectPath), "WPF sample project file이 존재해야 한다.");

            XDocument document = XDocument.Load(projectPath);
            XElement project = document.Root!;

            Assert.Equal("Microsoft.NET.Sdk", (string?)project.Attribute("Sdk"));
            Assert.Equal("net9.0-windows", ReadProperty(project, "TargetFramework"));
            Assert.Equal("true", ReadProperty(project, "UseWPF"));
            Assert.Equal("WinExe", ReadProperty(project, "OutputType"));
            Assert.Equal("disable", ReadProperty(project, "ImplicitUsings"));
        }

        [Fact]
        public void Solution_WhenInspected_IncludesDashboardProjects()
        {
            // 새 sample과 테스트 project가 solution에 빠지면 전체 build/test 검증에서 제외된다.
            string solutionPath = Path.GetFullPath(Path.Combine(
                AppContext.BaseDirectory,
                "..",
                "..",
                "..",
                "..",
                "..",
                "HighPerformanceSocket.slnx"));

            string solution = File.ReadAllText(solutionPath);

            Assert.Contains("samples/Hps.Sample.Dashboard/Hps.Sample.Dashboard.csproj", solution);
            Assert.Contains("tests/Hps.Sample.Dashboard.Tests/Hps.Sample.Dashboard.Tests.csproj", solution);
        }

        [Fact]
        public void DashboardCopy_WhenInspected_ExplainsSmokeCommandsAreIndependentLoopbackChecks()
        {
            // smoke 버튼은 Start server로 띄운 endpoint가 아니라 독립 loopback 서버를 잠깐 만들어 검증한다.
            // UI와 README가 이 차이를 설명해야 사용자가 현재 server diagnostics와 smoke 결과를 혼동하지 않는다.
            string sampleRoot = Path.GetFullPath(Path.Combine(
                AppContext.BaseDirectory,
                "..",
                "..",
                "..",
                "..",
                "..",
                "samples",
                "Hps.Sample.Dashboard"));
            string xaml = File.ReadAllText(Path.Combine(sampleRoot, "MainWindow.xaml"));
            string readme = File.ReadAllText(Path.Combine(sampleRoot, "README.md"));

            Assert.Contains("독립 loopback smoke", xaml);
            Assert.Contains("Start server와 별개", readme);
            Assert.Contains("임시 loopback server", readme);
        }

        private static string? ReadProperty(XElement project, string name)
        {
            return project
                .Elements("PropertyGroup")
                .Elements(name)
                .Select(element => element.Value)
                .FirstOrDefault();
        }
    }
}
