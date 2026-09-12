using System.Linq;
using Xunit;
using DshLauncher.Services;

namespace DshLauncher.Tests
{
    public class PortExclusionServiceTests
    {
        private const string Fixture = @"
Protocol tcp Port Exclusion Ranges

Start Port    End Port
----------    --------
      2985        3084
      3080        3080     *
      5357        5357
     50000       50059     *

* - Administered port exclusions.
";

        [Fact]
        public void Parse_ExtractsRangesAndAdminMarks()
        {
            var ranges = PortExclusionService.Parse(Fixture);

            Assert.Equal(4, ranges.Count);
            Assert.Contains(ranges, r => r.Start == 2985 && r.End == 3084 && !r.IsAdminExclusion);
            Assert.Contains(ranges, r => r.Start == 3080 && r.End == 3080 && r.IsAdminExclusion);
        }

        [Fact]
        public void Classify_PortInSystemRange_IsSystemReserved()
        {
            Assert.Equal(PortExclusionStatus.SystemReserved, PortExclusionService.Classify(3000, Fixture));
        }

        [Fact]
        public void Classify_PortWithAdminExclusion_IsAdminExcluded()
        {
            Assert.Equal(PortExclusionStatus.AdminExcluded, PortExclusionService.Classify(3080, Fixture));
        }

        [Fact]
        public void Classify_PortOutsideAllRanges_IsNone()
        {
            Assert.Equal(PortExclusionStatus.None, PortExclusionService.Classify(3390, Fixture));
        }

        [Fact]
        public void BuildScript_ContainsTheFixSequence()
        {
            var script = PortExclusionFixer.BuildScript(3080);

            Assert.Contains("net stop winnat", script);
            Assert.Contains("netsh int ipv4 add excludedportrange protocol=tcp startport=3080 numberofports=1 store=persistent", script);
            Assert.Contains("net start winnat", script);
        }
    }

    public class DshProcessManagerCommandTests
    {
        [Fact]
        public void BuildStartScript_RedirectsToLogFile()
        {
            var script = DshProcessManager.BuildStartScript(
                @"C:\node.exe", @"C:\dsh\bin.js", @"C:\logs\dsh.log", 3090, "127.0.0.1", @"C:\Users\me\.dsh");

            Assert.StartsWith("@echo off", script);
            Assert.Contains("set \"DSH_HOME=C:\\Users\\me\\.dsh\"", script);
            Assert.Contains("\"C:\\node.exe\" \"C:\\dsh\\bin.js\" ", script);
            Assert.Contains("web --port 3090 --host 127.0.0.1 --no-open", script);
            Assert.Contains(">> \"C:\\logs\\dsh.log\" 2>&1", script);
        }
    }
}
