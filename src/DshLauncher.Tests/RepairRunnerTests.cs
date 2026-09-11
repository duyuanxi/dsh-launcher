using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Xunit;
using DshLauncher.Services;

namespace DshLauncher.Tests
{
    public class RepairRunnerTests : IDisposable
    {
        private readonly string _root;
        private readonly string _profileDir;
        private readonly string _cliDir;
        private readonly string _configPath;
        private readonly AppSettings _settings;
        private readonly SettingsStore _store;
        private readonly PluginManager _pluginManager;
        private readonly string _dshWin32CliPath;

        public RepairRunnerTests()
        {
            _root = Path.Combine(Path.GetTempPath(), "dshlauncher-tests", Guid.NewGuid().ToString("N"));
            _profileDir = Path.Combine(_root, "profiles", "web");
            _cliDir = Path.Combine(_root, "cli");
            _configPath = Path.Combine(_root, "config.json");
            Directory.CreateDirectory(_profileDir);
            Directory.CreateDirectory(_cliDir);

            File.WriteAllText(Path.Combine(_profileDir, "package.json"), @"{
  ""name"": ""dsh-profile-web"",
  ""dependencies"": { ""dshmarket"": ""^1.0.0"" },
  ""dsh"": { ""profile"": { ""bundles"": [""@deepseek-ai/dsh-base"", ""dshmarket""] } }
}");
            File.WriteAllText(Path.Combine(_cliDir, "package.json"), @"{ ""dependencies"": { ""@deepseek-ai/dsh-base"": ""0.1.1"" } }");

            _dshWin32CliPath = Path.Combine(_root, "dsh-win32", "bin", "cli.mjs");
            Directory.CreateDirectory(Path.GetDirectoryName(_dshWin32CliPath)!);
            File.WriteAllText(_dshWin32CliPath, "x");

            _settings = new AppSettings { NodePath = "stale", DshBinPath = "stale", DshVersion = "stale" };
            _store = new SettingsStore(_configPath);
            _pluginManager = new PluginManager(_profileDir, _cliDir);
        }

        public void Dispose()
        {
            if (Directory.Exists(_root))
            {
                Directory.Delete(_root, true);
            }
        }

        private static RepairRunner MakeRunner(
            AppSettings settings, SettingsStore store, PluginManager pm, string cliPath,
            RepairRunner.CommandRunner run, Func<DshLocation?> resolve)
        {
            return new RepairRunner(settings, store, pm, cliPath, run, null, resolve);
        }

        [Fact]
        public void AllSucceed_ReportsStepsInOrderAndSavesResolvedPaths()
        {
            var calls = new List<(string script, string[] args)>();
            var runner = MakeRunner(_settings, _store, _pluginManager, _dshWin32CliPath,
                (script, args) => { calls.Add((script, args)); return (0, ""); },
                () => new DshLocation(@"C:\node.exe", @"C:\dsh\bin.js", "0.1.1"));

            var report = runner.Run();

            Assert.True(report.AllSucceeded);
            Assert.Equal(
                new[] { "检查 dsh 安装", "清理卸载残留", "重装依赖", "修复 koffi" },
                report.Steps.Select(s => s.StepName));

            Assert.Contains(calls, c => c.script == _dshWin32CliPath && c.args.SequenceEqual(new[] { "fix" }));
            Assert.Contains(calls, c => c.script == @"C:\dsh\bin.js" && c.args.SequenceEqual(new[] { "plugin", "--profile", "web", "install" }));

            var saved = _store.Load();
            Assert.Equal(@"C:\node.exe", saved.NodePath);
            Assert.Equal(@"C:\dsh\bin.js", saved.DshBinPath);
            Assert.Equal("0.1.1", saved.DshVersion);
        }

        [Fact]
        public void MissingDsh_FailsResolveAndSkipsReinstall()
        {
            var runner = MakeRunner(_settings, _store, _pluginManager, _dshWin32CliPath,
                (script, args) => (0, ""),
                () => null);

            var report = runner.Run();

            Assert.False(report.AllSucceeded);
            Assert.False(report.Steps.First(s => s.StepName == "检查 dsh 安装").Succeeded);
            Assert.False(report.Steps.First(s => s.StepName == "重装依赖").Succeeded);
        }

        [Fact]
        public void Leftovers_AreRemovedFromManifest()
        {
            var manifestPath = Path.Combine(_profileDir, "package.json");
            File.WriteAllText(manifestPath, @"{
  ""name"": ""dsh-profile-web"",
  ""dependencies"": { ""dshmarket"": ""^1.0.0"" },
  ""dsh"": { ""profile"": { ""bundles"": [""@deepseek-ai/dsh-base"", ""dshmarket"", ""ghost-plugin""] } }
}");

            var runner = MakeRunner(_settings, _store, _pluginManager, _dshWin32CliPath,
                (script, args) => (0, ""),
                () => new DshLocation("node", "bin", "1"));

            var report = runner.Run();

            var step = report.Steps.First(s => s.StepName == "清理卸载残留");
            Assert.True(step.Succeeded);
            Assert.Contains("1 个", step.Detail);
            Assert.DoesNotContain("ghost-plugin", File.ReadAllText(manifestPath));
        }

        [Fact]
        public void CommandFailure_ContinuesSequenceAndReports()
        {
            var runner = MakeRunner(_settings, _store, _pluginManager, _dshWin32CliPath,
                (script, args) => args.Length == 1 ? (1, "boom") : (0, ""),
                () => new DshLocation("node", "bin", "1"));

            var report = runner.Run();

            Assert.False(report.AllSucceeded);
            Assert.False(report.Steps.First(s => s.StepName == "修复 koffi").Succeeded);
            Assert.True(report.Steps.First(s => s.StepName == "重装依赖").Succeeded);
        }

        [Fact]
        public void MissingDshWin32_SkipsKoffiFix()
        {
            var runner = MakeRunner(_settings, _store, _pluginManager, Path.Combine(_root, "nope.mjs"),
                (script, args) => (0, ""),
                () => new DshLocation("node", "bin", "1"));

            var report = runner.Run();

            var step = report.Steps.First(s => s.StepName == "修复 koffi");
            Assert.False(step.Succeeded);
            Assert.Contains("未安装", step.Detail);
        }
    }
}
