using System;
using System.Collections.Generic;
using System.IO;

namespace DshLauncher.Services
{
    public class InstallerPlan
    {
        public bool NodeFound { get; }
        public bool DshInstalled { get; }
        public bool AlreadyInstalled { get; }
        public IReadOnlyList<string> Steps { get; }

        public bool CanInstall => NodeFound;

        public InstallerPlan(bool nodeFound, bool dshInstalled, bool alreadyInstalled, IReadOnlyList<string> steps)
        {
            NodeFound = nodeFound;
            DshInstalled = dshInstalled;
            AlreadyInstalled = alreadyInstalled;
            Steps = steps;
        }
    }

    /// <summary>
    /// Pure planning logic for the one-click installer, plus installed-state
    /// queries. All filesystem/process side effects live in the UI layer.
    /// </summary>
    public static class InstallerService
    {
        public static string InstalledExePath => Path.Combine(AppPaths.AppDataDir, "DshLauncher.exe");

        /// <summary>The launcher counts as installed once its config file exists.</summary>
        public static bool IsInstalled() => File.Exists(SettingsStore.DefaultFilePath);

        public static InstallerPlan Plan(bool nodeFound, bool dshInstalled, bool alreadyInstalled)
        {
            var steps = new List<string>();
            if (!nodeFound)
            {
                steps.Add("需要先安装 Node.js（版本 ≥ 22.15，含内置 zstd 支持）");
            }
            else
            {
                if (!dshInstalled)
                {
                    steps.Add("安装 @deepseek-ai/dsh 到全局 npm");
                }
                steps.Add("复制启动器到 %LOCALAPPDATA%\\DSHLauncher");
                steps.Add("创建桌面快捷方式（可选）");
                steps.Add("开机自启（可选）");
            }

            return new InstallerPlan(nodeFound, dshInstalled, alreadyInstalled, steps);
        }
    }
}
