using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using DshLauncher.Services;

namespace DshLauncher
{
    /// <summary>
    /// One-click installer wizard: installs global dsh, copies the launcher to
    /// %LOCALAPPDATA%\DSHLauncher, creates a desktop shortcut, and optionally
    /// enables autostart.
    /// </summary>
    public partial class InstallerWindow : Window
    {
        private readonly string? _nodePath;
        private readonly bool _dshInstalled;
        private readonly bool _alreadyInstalled;

        public bool RunPortableRequested { get; private set; }

        public InstallerWindow()
        {
            InitializeComponent();

            _nodePath = DshResolver.FindNodePath();
            _dshInstalled = new DshResolver().Resolve() != null;
            _alreadyInstalled = InstallerService.IsInstalled();

            var plan = InstallerService.Plan(_nodePath != null, _dshInstalled, _alreadyInstalled);
            IntroText.Text =
                "本向导将把 DSH Launcher 安装到本机（复制到 %LOCALAPPDATA%\\DSHLauncher 并创建快捷方式）。"
                + (_dshInstalled ? "" : " 检测到尚未安装 @deepseek-ai/dsh，将一并自动安装。")
                + (_alreadyInstalled ? "\n\n注意：本机已存在安装（可能是旧版本），继续将覆盖升级。" : "");

            foreach (var step in plan.Steps)
            {
                Log("• " + step);
            }

            if (_nodePath == null)
            {
                Log("\n未检测到 Node.js（需要 ≥ 22.15）。请先安装后再重新运行本向导。");
                InstallButton.IsEnabled = false;
                NodeButton.Visibility = Visibility.Visible;
            }
            else
            {
                Log("\n检测到 Node.js：" + _nodePath);
            }

            var portStatus = PortExclusionService.Check(3080);
            if (portStatus == PortExclusionStatus.SystemReserved)
            {
                Log("\n⚠ 检测到端口 3080 落在 Windows 保留端口段（Hyper-V/WSL2 的 winnat 服务动态保留），harness 启动会报 EACCES。建议先点「修复端口保留」。");
                PortFixButton.Visibility = Visibility.Visible;
            }
        }

        private void PortFixButton_Click(object sender, RoutedEventArgs e)
        {
            Log("\n── 修复端口保留（将弹出 UAC 管理员授权，请点「是」）…");
            var ok = PortExclusionFixer.Run(3080);
            var after = PortExclusionService.Check(3080);
            if (ok && after != PortExclusionStatus.SystemReserved)
            {
                Log("✔ 端口 3080 已永久移出 winnat 动态保留段（重启后仍有效）");
                PortFixButton.Visibility = Visibility.Collapsed;
            }
            else
            {
                Log("✘ 修复失败（UAC 被取消或 winnat 操作失败）。可稍后重试，或改用其它端口。");
            }
        }

        private void Log(string line)
        {
            LogBox.AppendText(line + Environment.NewLine);
            LogBox.ScrollToEnd();
        }

        private async void InstallButton_Click(object sender, RoutedEventArgs e)
        {
            InstallButton.IsEnabled = false;
            PortableButton.IsEnabled = false;
            try
            {
                if (!_dshInstalled)
                {
                    Log("\n── 安装 @deepseek-ai/dsh 到全局 npm …");
                    var (code, output) = await Task.Run(() => RunCmd("npm install -g @deepseek-ai/dsh"));
                    if (!string.IsNullOrWhiteSpace(output))
                    {
                        Log(output.TrimEnd());
                    }
                    if (code != 0)
                    {
                        Log("✘ dsh 安装失败（exit " + code + "），请检查网络后重试。");
                        InstallButton.IsEnabled = true;
                        PortableButton.IsEnabled = true;
                        return;
                    }
                    Log("✔ dsh 安装完成");
                }
                else
                {
                    Log("\n✔ 检测到 @deepseek-ai/dsh 已安装，跳过");
                }

                Log("\n── 复制启动器到 " + AppPaths.AppDataDir + " …");
                SessionHelper.EnsureExtracted(AppContext.BaseDirectory);
                CopyAppFiles();
                Log("✔ 复制完成");

                if (ShortcutCheck.IsChecked == true)
                {
                    Log("── 创建桌面快捷方式 …");
                    CreateShortcut();
                    Log("✔ 快捷方式已创建");
                }

                if (AutostartCheck.IsChecked == true)
                {
                    Log("── 启用开机自启 …");
                    new AutostartManager().Enable(InstallerService.InstalledExePath);
                    Log("✔ 开机自启已启用");
                }

                Log("\n✅ 安装完成！点击「启动 DeepSeek Harness」开始使用。");
                LaunchButton.Visibility = Visibility.Visible;
                ShortcutCheck.IsEnabled = false;
                AutostartCheck.IsEnabled = false;
            }
            catch (Exception ex)
            {
                Log("\n✘ 安装失败: " + ex.Message);
                InstallButton.IsEnabled = true;
                PortableButton.IsEnabled = true;
            }
        }

        private void CopyAppFiles()
        {
            Directory.CreateDirectory(AppPaths.AppDataDir);
            foreach (var file in Directory.GetFiles(AppContext.BaseDirectory))
            {
                if (file.EndsWith(".pdb", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }
                var dest = Path.Combine(AppPaths.AppDataDir, Path.GetFileName(file));
                try
                {
                    File.Copy(file, dest, overwrite: true);
                }
                catch (IOException)
                {
                    throw new IOException("目标文件被占用——请先退出正在运行的 DSH Launcher 后再安装。");
                }
            }
        }

        private void CreateShortcut()
        {
            var desktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
            var path = Path.Combine(desktop, "DeepSeek Harness 启动器.lnk");
            var shellType = Type.GetTypeFromProgID("WScript.Shell")!;
            dynamic shell = Activator.CreateInstance(shellType)!;
            dynamic shortcut = shell.CreateShortcut(path);
            shortcut.TargetPath = InstallerService.InstalledExePath;
            shortcut.Arguments = "--start";
            shortcut.WorkingDirectory = AppPaths.AppDataDir;
            shortcut.Description = "一键启动 DeepSeek Harness";
            shortcut.Save();
        }

        private void LaunchButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                Process.Start(new ProcessStartInfo(InstallerService.InstalledExePath, "--start") { UseShellExecute = true });
            }
            catch (Exception ex)
            {
                Log("启动失败: " + ex.Message);
            }
            Close();
        }

        private void NodeButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                Process.Start(new ProcessStartInfo("https://nodejs.org/zh-cn/download") { UseShellExecute = true });
            }
            catch { /* ignore */ }
        }

        private void PortableButton_Click(object sender, RoutedEventArgs e)
        {
            RunPortableRequested = true;
            Close();
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e) => Close();

        private static (int exitCode, string output) RunCmd(string cmdCommand)
        {
            var psi = new ProcessStartInfo
            {
                FileName = "cmd.exe",
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8
            };
            psi.ArgumentList.Add("/c");
            psi.ArgumentList.Add(cmdCommand);

            using var p = new Process { StartInfo = psi };
            var sb = new StringBuilder();
            p.OutputDataReceived += (_, ev) => { if (ev.Data != null) { lock (sb) { sb.AppendLine(ev.Data); } } };
            p.ErrorDataReceived += (_, ev) => { if (ev.Data != null) { lock (sb) { sb.AppendLine(ev.Data); } } };
            p.Start();
            p.BeginOutputReadLine();
            p.BeginErrorReadLine();
            p.WaitForExit();
            lock (sb) { return (p.ExitCode, sb.ToString()); }
        }
    }
}
