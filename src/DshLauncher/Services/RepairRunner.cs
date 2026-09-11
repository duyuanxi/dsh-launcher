using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace DshLauncher.Services
{
    public class RepairStepResult
    {
        public string StepName { get; }
        public bool Succeeded { get; }
        public string Detail { get; }

        public RepairStepResult(string stepName, bool succeeded, string detail)
        {
            StepName = stepName;
            Succeeded = succeeded;
            Detail = detail;
        }
    }

    public class RepairReport
    {
        public IReadOnlyList<RepairStepResult> Steps { get; }
        public bool AllSucceeded => Steps.All(s => s.Succeeded);

        public RepairReport(IReadOnlyList<RepairStepResult> steps)
        {
            Steps = steps;
        }
    }

    /// <summary>
    /// One-click repair sequence: re-resolve the dsh installation, clean
    /// leftover bundle entries, reinstall profile dependencies, and repair the
    /// koffi native module. Command execution and dsh resolution are injected
    /// so the orchestration is unit-testable.
    /// </summary>
    public class RepairRunner
    {
        public delegate (int exitCode, string output) CommandRunner(string scriptPath, string[] args);

        private readonly AppSettings _settings;
        private readonly SettingsStore _store;
        private readonly PluginManager _pluginManager;
        private readonly string _dshWin32CliPath;
        private readonly CommandRunner _runCommand;
        private readonly Action<string>? _log;
        private readonly Func<DshLocation?> _resolveDsh;

        public RepairRunner(
            AppSettings settings,
            SettingsStore store,
            PluginManager pluginManager,
            string dshWin32CliPath,
            CommandRunner runCommand,
            Action<string>? log = null,
            Func<DshLocation?>? resolveDsh = null)
        {
            _settings = settings;
            _store = store;
            _pluginManager = pluginManager;
            _dshWin32CliPath = dshWin32CliPath;
            _runCommand = runCommand;
            _log = log;
            _resolveDsh = resolveDsh ?? (() => new DshResolver().Resolve());
        }

        public RepairReport Run()
        {
            var steps = new List<RepairStepResult>();

            // 1. re-resolve dsh paths (fixes stale/broken config paths)
            _log?.Invoke("── 检查 dsh 安装…");
            var loc = _resolveDsh();
            if (loc == null)
            {
                steps.Add(new RepairStepResult("检查 dsh 安装", false, "未找到 node.exe 或全局 @deepseek-ai/dsh"));
            }
            else
            {
                _settings.NodePath = loc.NodePath;
                _settings.DshBinPath = loc.BinPath;
                _settings.DshVersion = loc.Version;
                _store.Save(_settings);
                steps.Add(new RepairStepResult("检查 dsh 安装", true, $"dsh {loc.Version}（{loc.NodePath}）"));
            }

            // 2. clean leftover bundle entries
            _log?.Invoke("── 扫描卸载残留…");
            var leftovers = _pluginManager.ScanLeftovers();
            if (leftovers.Count == 0)
            {
                steps.Add(new RepairStepResult("清理卸载残留", true, "未发现残留"));
            }
            else
            {
                _pluginManager.RemoveStaleBundles(leftovers);
                steps.Add(new RepairStepResult("清理卸载残留", true, $"已移除 {leftovers.Count} 个残留条目：{string.Join(", ", leftovers)}"));
            }

            // 3. reinstall dependencies (pnpm install + bundles reconcile)
            _log?.Invoke("── 重装依赖 (pnpm install)…");
            if (loc == null)
            {
                steps.Add(new RepairStepResult("重装依赖", false, "dsh 未安装，跳过"));
            }
            else
            {
                var (code, output) = _runCommand(loc.BinPath, new[] { "plugin", "--profile", AppPaths.ProfileName, "install" });
                steps.Add(new RepairStepResult("重装依赖", code == 0, "exit " + code));
                LogOutput(output);
            }

            // 4. repair the koffi native module
            _log?.Invoke("── 修复 koffi 原生模块 (dsh-win32 fix)…");
            if (!File.Exists(_dshWin32CliPath))
            {
                steps.Add(new RepairStepResult("修复 koffi", false, "未安装 dsh-win32 插件，跳过"));
            }
            else
            {
                var (code2, output2) = _runCommand(_dshWin32CliPath, new[] { "fix" });
                steps.Add(new RepairStepResult("修复 koffi", code2 == 0, "exit " + code2));
                LogOutput(output2);
            }

            return new RepairReport(steps);
        }

        private void LogOutput(string output)
        {
            var trimmed = output.TrimEnd();
            if (trimmed.Length > 0)
            {
                _log?.Invoke(trimmed);
            }
        }
    }
}
