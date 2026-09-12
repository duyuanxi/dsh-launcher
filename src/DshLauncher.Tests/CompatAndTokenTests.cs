using System;
using System.IO;
using System.Linq;
using Xunit;
using DshLauncher.Services;

namespace DshLauncher.Tests
{
    public class SemVerRangeTests
    {
        [Theory]
        [InlineData("4.0.2", "^4.0.2", true)]
        [InlineData("4.1.0", "^4.0.2", true)]
        [InlineData("5.0.0", "^4.0.2", false)]
        [InlineData("0.1.5-rc.2", "^0.1.5-rc.1", true)]
        [InlineData("0.1.5", "^0.1.5-rc.1", true)]
        [InlineData("0.1.4", "^0.1.5-rc.1", false)]
        [InlineData("0.2.0", "^0.1.5-rc.1", false)]
        [InlineData("18.3.1", "^18.3.1", true)]
        [InlineData("19.0.0", "^18.3.1", false)]
        [InlineData("0.1.5-rc.2", ">=0.1.0-rc.5 <0.2.0", true)]
        [InlineData("0.2.0", ">=0.1.0-rc.5 <0.2.0", false)]
        [InlineData("0.1.5-rc.2", ">=0.1.0-rc.8 <0.2.0 || ^0.1.1-rc.1 || ^0.1.2-alpha.1 || ^0.1.5-rc.1", true)]
        [InlineData("0.1.5-rc.2", ">=0.1.0-rc.5 <0.1.0-rc.7", false)]
        [InlineData("3.18.2", "^3.18.1", true)]
        [InlineData("4.0.2", "*", true)]
        [InlineData("1.2.3", "1.2.3", true)]
        [InlineData("1.2.4", "1.2.3", false)]
        [InlineData("1.2.9", "1.2", true)]
        [InlineData("1.3.0", "1.2", false)]
        [InlineData("2.0.0", "1", false)]
        [InlineData("1.9.9", "1", true)]
        public void Satisfies_MatchesExpected(string version, string range, bool expected)
        {
            Assert.Equal(expected, SemVerRange.Satisfies(version, range));
        }

        [Fact]
        public void Satisfies_InvalidInputs_ReturnFalse()
        {
            Assert.False(SemVerRange.Satisfies("abc", "*"));
            Assert.False(SemVerRange.Satisfies("1.0.0", ""));
            Assert.False(SemVerRange.Satisfies("1.0.0", "not-a-range"));
        }
    }

    public class PluginCompatCheckerTests : IDisposable
    {
        private readonly string _root;
        private readonly string _profileDir;
        private readonly string _cliDir;

        public PluginCompatCheckerTests()
        {
            _root = Path.Combine(Path.GetTempPath(), "dshlauncher-tests", Guid.NewGuid().ToString("N"));
            _profileDir = Path.Combine(_root, "profiles", "web");
            _cliDir = Path.Combine(_root, "cli");
            Directory.CreateDirectory(_profileDir);
            Directory.CreateDirectory(_cliDir);
        }

        public void Dispose()
        {
            if (Directory.Exists(_root))
            {
                Directory.Delete(_root, true);
            }
        }

        private void WritePlugin(string name, string peersJson)
        {
            var dir = Path.Combine(_profileDir, "node_modules", name);
            Directory.CreateDirectory(dir);
            File.WriteAllText(Path.Combine(dir, "package.json"),
                @"{ ""name"": """ + name + @""", ""version"": ""1.0.0"", ""peerDependencies"": " + peersJson + " }");
        }

        private void WriteHarnessPackage(string name, string version)
        {
            var dir = Path.Combine(_cliDir, "node_modules", name);
            Directory.CreateDirectory(dir);
            File.WriteAllText(Path.Combine(dir, "package.json"), @"{ ""name"": """ + name + @""", ""version"": """ + version + @""" }");
        }

        [Fact]
        public void Check_CompatiblePeers_NoIssues()
        {
            File.WriteAllText(Path.Combine(_profileDir, "package.json"),
                @"{ ""dependencies"": { ""dshmarket"": ""^1.0.0"" } }");
            WritePlugin("dshmarket", @"{ ""@deepseek-ai/dsh-session"": ""^0.1.5-rc.1"" }");
            WriteHarnessPackage("@deepseek-ai/dsh-session", "0.1.5-rc.2");

            var issues = PluginCompatChecker.Check(_profileDir, _cliDir);

            Assert.Empty(issues);
        }

        [Fact]
        public void Check_IncompatiblePeer_ReportsIssue()
        {
            File.WriteAllText(Path.Combine(_profileDir, "package.json"),
                @"{ ""dependencies"": { ""dshmarket"": ""^1.0.0"" } }");
            WritePlugin("dshmarket", @"{ ""@deepseek-ai/dsh-session"": ""^0.2.0"" }");
            WriteHarnessPackage("@deepseek-ai/dsh-session", "0.1.5-rc.2");

            var issues = PluginCompatChecker.Check(_profileDir, _cliDir);

            var issue = Assert.Single(issues);
            Assert.Equal("dshmarket", issue.PluginName);
            Assert.Equal("@deepseek-ai/dsh-session", issue.PeerName);
            Assert.Equal("0.1.5-rc.2", issue.InstalledVersion);
        }

        [Fact]
        public void Check_MissingDeepseekPeer_ReportsNotInstalled()
        {
            File.WriteAllText(Path.Combine(_profileDir, "package.json"),
                @"{ ""dependencies"": { ""dshmarket"": ""^1.0.0"" } }");
            WritePlugin("dshmarket", @"{ ""@deepseek-ai/dsh-session"": ""^0.1.0"", ""react"": ""^18.0.0"" }");
            // neither peer installed anywhere

            var issues = PluginCompatChecker.Check(_profileDir, _cliDir);

            var issue = Assert.Single(issues); // only the @deepseek-ai one is reported
            Assert.Equal("未安装", issue.InstalledVersion);
        }
    }

    public class TokenUrlTests
    {
        [Fact]
        public void ParseWebUrl_ExtractsTokenUrl()
        {
            Assert.Equal(
                "http://127.0.0.1:3080/?token=AbC123",
                HealthChecker.ParseWebUrl("dsh web: http://127.0.0.1:3080/?token=AbC123"));
        }

        [Fact]
        public void FindTokenUrl_ReturnsLastDshWebUrl()
        {
            var lines = new[]
            {
                "old noise",
                "dsh web: http://127.0.0.1:3080/?token=OLD",
                "more noise",
                "dsh web: http://127.0.0.1:3080/?token=NEW"
            };

            Assert.Equal("http://127.0.0.1:3080/?token=NEW", DshController.FindTokenUrl(lines));
        }

        [Fact]
        public void FindTokenUrl_NoStartupLine_ReturnsNull()
        {
            Assert.Null(DshController.FindTokenUrl(new[] { "no url here", "still nothing" }));
        }
    }
}
