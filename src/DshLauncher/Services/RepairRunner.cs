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
        private readonly Func<int, PortExclusionStatus> _checkPort;
        private readonly Func<int, bool> _fixPort;
        private readonly Func<IReadOnlyList<PluginCompatIssue>> _checkCompat;

        public RepairRunner(
            AppSettings settings,
            SettingsStore store,
            PluginManager pluginManager,
            string dshWin32CliPath,
            CommandRunner runCommand,
            Action<string>? log = null,
            Func<DshLocation?>? resolveDsh = null,
            Func<int, PortExclusionStatus>? checkPort = null,
            Func<int, bool>? fixPort = null,
            Func<IReadOnlyList<PluginCompatIssue>>? checkCompat = null)
        {
            _settings = settings;
            _store = store;
            _pluginManager = pluginManager;
            _dshWin32CliPath = dshWin32CliPath;
            _runCommand = runCommand;
            _log = log;
            _resolveDsh = resolveDsh ?? (() => new DshResolver().Resolve());
            _checkPort = checkPort ?? (port => PortExclusionService.Check(port));
            _fixPort = fixPort ?? (port => PortExclusionFixer.Run(port));
            _checkCompat = checkCompat ?? (() =>
            {
                var cliDir = Path.GetDirectoryName(Path.GetDirectoryName(_settings.DshBinPath)) ?? "";
                return PluginCompatChecker.Check(AppPaths.ProfileDir, cliDir);
            });
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

            // 2. check the port against Windows reserved ranges (winnat EACCES issue)
            _log?.Invoke("── 检查端口保留段…");
            var portStatus = _checkPort(_settings.Port);
            if (portStatus != PortExclusionStatus.SystemReserved)
            {
                var portDetail = portStatus == PortExclusionStatus.AdminExcluded
                    ? $"端口 {_settings.Port} 已从系统保留段排除（此前已修复）"
                    : $"端口 {_settings.Port} 未被 Windows 保留";
                steps.Add(new RepairStepResult("检查端口保留段", true, portDetail));
            }
            else
            {
                _log?.Invoke($"⚠ 端口 {_settings.Port} 落在 Windows 保留端口段内（Hyper-V/WSL2 的 winnat 服务），将尝试修复（会弹出 UAC 管理员授权）…");
                var fixedOk = _fixPort(_settings.Port);
                var after = _checkPort(_settings.Port);
                var ok = fixedOk && after != PortExclusionStatus.SystemReserved;
                steps.Add(new RepairStepResult(
                    "检查端口保留段",
                    ok,
                    ok ? $"已把端口 {_settings.Port} 永久移出 winnat 动态保留段（重启后仍有效）" : "修复失败（UAC 被取消或 winnat 操作失败），端口可能仍无法绑定"));
            }

            // 3. clean leftover bundle entries
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

            // 5. plugin / harness version compatibility
            _log?.Invoke("── 检查插件兼容性…");
            var compatIssues = _checkCompat();
            if (compatIssues.Count == 0)
            {
                steps.Add(new RepairStepResult("检查插件兼容性", true, "所有插件与 harness 版本兼容"));
            }
            else
            {
                foreach (var issue in compatIssues)
                {
                    _log?.Invoke("⚠ " + issue);
                }

                var affected = compatIssues.Select(i => i.PluginName).Distinct().ToList();
                var updatesOk = loc != null;
                if (loc != null)
                {
                    foreach (var name in affected)
                    {
                        _log?.Invoke($"── 更新插件 {name} …");
                        var (code, output) = _runCommand(loc.BinPath, new[] { "plugin", "--profile", AppPaths.ProfileName, "update", name });
                        LogOutput(output);
                        if (code != 0)
                        {
                            updatesOk = false;
                        }
                    }
                }

                var remaining = _checkCompat();
                if (remaining.Count == 0)
                {
                    steps.Add(new RepairStepResult("检查插件兼容性", true, $"已更新 {affected.Count} 个插件，兼容性问题全部解决"));
                }
                else
                {
                    var detail = string.Join("；", remaining.Select(i => i.ToString()));
                    steps.Add(new RepairStepResult("检查插件兼容性", false, $"仍有 {remaining.Count} 处不兼容：{detail}"));
                }
            }

            // 6. repair the koffi native module
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
