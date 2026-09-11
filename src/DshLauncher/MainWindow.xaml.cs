using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using Microsoft.Win32;
using DshLauncher.Services;

namespace DshLauncher
{
    public partial class MainWindow : Window
    {
        private readonly DshController _controller;
        private readonly AppSettings _settings;
        private readonly SettingsStore _store;
        private readonly Notifier _notifier;
        private readonly System.Windows.Forms.NotifyIcon _trayIcon;
        private bool _allowClose;
        private bool _autoOpenOnce;
        private bool _trayHintShown;
        private PluginManager? _pluginManager;
        private SessionTitleReader? _sessionTitleReader;
        private bool _repairRunning;

        public MainWindow(DshController controller, AppSettings settings, SettingsStore store)
        {
            InitializeComponent();

            _controller = controller;
            _settings = settings;
            _store = store;

            _trayIcon = BuildTrayIcon();
            _notifier = new Notifier(_trayIcon);
            _pluginManager = new PluginManager(AppPaths.ProfileDir, CliPackageDir());
            _sessionTitleReader = new SessionTitleReader(_settings.NodePath, Path.Combine(AppContext.BaseDirectory, "session-reader.mjs"));

            LoadSettingsToUi();

            _controller.StateChanged += s => Dispatcher.Invoke(() => HandleDshState(s));
            _controller.LogLine += line => Dispatcher.Invoke(() => AppendLog(line));
            _controller.ResolvedUrlChanged += url => Dispatcher.Invoke(() => OnUrlChanged(url));

            HandleDshState(_controller.State);
            OnUrlChanged(_controller.ResolvedUrl ?? "");

            LoadLogTail();
            _ = RefreshSessionsAsync();
            UpdateVersionText();
            RefreshPlugins();
        }

        // ---------- 初始化 ----------

        private void LoadSettingsToUi()
        {
            PortBox.Text = _settings.Port.ToString();
            HostBox.Text = _settings.Host;
            DelayBox.Text = _settings.AutostartDelaySeconds.ToString();
            AutoOpenBrowserCheck.IsChecked = _settings.AutoOpenBrowser;
            AutostartCheck.IsChecked = _settings.AutostartEnabled;
            AutostartSilentCheck.IsChecked = _settings.AutostartSilent;
            WatchdogCheck.IsChecked = _settings.WatchdogEnabled;
            MinimizeToTrayCheck.IsChecked = _settings.MinimizeToTray;
        }

        private System.Windows.Forms.NotifyIcon BuildTrayIcon()
        {
            var menu = new System.Windows.Forms.ContextMenuStrip();
            menu.Items.Add("打开主界面", null, (_, __) => ShowFromTray());
            menu.Items.Add(new System.Windows.Forms.ToolStripSeparator());
            menu.Items.Add("启动", null, (_, __) => StartClicked());
            menu.Items.Add("停止", null, (_, __) => _controller.Stop());
            menu.Items.Add("重启", null, (_, __) => { if (ReadPortHostFromUi()) _controller.Restart(); });
            menu.Items.Add("打开网页", null, (_, __) => OpenBrowser());
            menu.Items.Add("一键修复", null, (_, __) => { ShowFromTray(); OneClickRepair(); });
            menu.Items.Add(new System.Windows.Forms.ToolStripSeparator());

            var autostartItem = new System.Windows.Forms.ToolStripMenuItem("开机自启")
            {
                CheckOnClick = true,
                Checked = _settings.AutostartEnabled
            };
            autostartItem.CheckedChanged += (_, __) => SetAutostart(autostartItem.Checked);
            menu.Items.Add(autostartItem);

            var watchdogItem = new System.Windows.Forms.ToolStripMenuItem("守护模式")
            {
                CheckOnClick = true,
                Checked = _settings.WatchdogEnabled
            };
            watchdogItem.CheckedChanged += (_, __) => SetWatchdog(watchdogItem.Checked);
            menu.Items.Add(watchdogItem);

            menu.Items.Add(new System.Windows.Forms.ToolStripSeparator());
            menu.Items.Add("退出", null, (_, __) => ExitApplication());

            var icon = new System.Windows.Forms.NotifyIcon
            {
                Icon = System.Drawing.SystemIcons.Application,
                Text = "DeepSeek Harness 启动器",
                ContextMenuStrip = menu,
                Visible = true
            };
            icon.DoubleClick += (_, __) => ShowFromTray();
            return icon;
        }

        // ---------- 状态 ----------

        private void HandleDshState(DshState state)
        {
            switch (state)
            {
                case DshState.Stopped:
                    StatusText.Text = "已停止";
                    StatusDot.Fill = Brushes.Gray;
                    break;
                case DshState.Starting:
                    StatusText.Text = "启动中…";
                    StatusDot.Fill = Brushes.Orange;
                    break;
                case DshState.Running:
                    StatusText.Text = "运行中";
                    StatusDot.Fill = Brushes.LimeGreen;
                    if (_autoOpenOnce)
                    {
                        _autoOpenOnce = false;
                        OpenBrowser();
                    }
                    break;
                case DshState.Error:
                    StatusText.Text = "异常";
                    StatusDot.Fill = Brushes.Red;
                    break;
            }

            StartButton.IsEnabled = state == DshState.Stopped || state == DshState.Error;
            StopButton.IsEnabled = state == DshState.Starting || state == DshState.Running;
            RestartButton.IsEnabled = state == DshState.Starting || state == DshState.Running;
            OpenButton.IsEnabled = state == DshState.Running;
        }

        private void OnUrlChanged(string url)
        {
            UrlText.Text = string.IsNullOrEmpty(url) ? "" : url;
        }

        // ---------- 操作 ----------

        private void StartButton_Click(object sender, RoutedEventArgs e) => StartClicked();

        /// <summary>One-click start from a <c>--start</c> launch: show then start dsh.</summary>
        public void StartFromLaunch()
        {
            StartClicked();
        }

        private void StartClicked()
        {
            if (_controller.State == DshState.Starting || _controller.State == DshState.Running)
            {
                return;
            }
            if (!ReadPortHostFromUi())
            {
                return;
            }
            _autoOpenOnce = _settings.AutoOpenBrowser;
            _controller.Start();
        }

        private void StopButton_Click(object sender, RoutedEventArgs e) => _controller.Stop();

        private void RestartButton_Click(object sender, RoutedEventArgs e)
        {
            if (ReadPortHostFromUi())
            {
                _autoOpenOnce = _settings.AutoOpenBrowser;
                _controller.Restart();
            }
        }

        private void OpenButton_Click(object sender, RoutedEventArgs e) => OpenBrowser();

        private void OpenBrowser()
        {
            var url = _controller.ResolvedUrl;
            if (string.IsNullOrEmpty(url))
            {
                return;
            }
            try
            {
                Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
            }
            catch (Exception ex)
            {
                _notifier.Notify("打开网页失败", ex.Message);
            }
        }

        // ---------- 设置 ----------

        private bool ReadPortHostFromUi()
        {
            if (!int.TryParse(PortBox.Text.Trim(), out var port) || port < 0 || port > 65535)
            {
                MessageBox.Show("端口必须是 0-65535 的整数。", "设置", MessageBoxButton.OK, MessageBoxImage.Warning);
                return false;
            }

            var host = HostBox.Text.Trim();
            if (string.IsNullOrEmpty(host))
            {
                MessageBox.Show("绑定地址不能为空。", "设置", MessageBoxButton.OK, MessageBoxImage.Warning);
                return false;
            }
            if (host == "0.0.0.0")
            {
                MessageBox.Show("dsh 出于安全不支持 0.0.0.0 绑定，请使用 127.0.0.1。", "设置", MessageBoxButton.OK, MessageBoxImage.Warning);
                return false;
            }

            _settings.Port = port;
            _settings.Host = host;
            return true;
        }

        private void SaveButton_Click(object sender, RoutedEventArgs e)
        {
            if (!ReadPortHostFromUi())
            {
                return;
            }
            if (!int.TryParse(DelayBox.Text.Trim(), out var delay) || delay < 0)
            {
                MessageBox.Show("开机延迟必须是 ≥0 的整数秒。", "设置", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            _settings.AutostartDelaySeconds = delay;
            _settings.AutoOpenBrowser = AutoOpenBrowserCheck.IsChecked == true;
            _settings.AutostartSilent = AutostartSilentCheck.IsChecked == true;
            _settings.WatchdogEnabled = WatchdogCheck.IsChecked == true;
            _settings.MinimizeToTray = MinimizeToTrayCheck.IsChecked == true;
            _settings.AutostartEnabled = AutostartCheck.IsChecked == true;

            _store.Save(_settings);
            SettingsHint.Text = "已保存 " + DateTime.Now.ToString("HH:mm:ss");
        }

        private void AutostartCheck_Changed(object sender, RoutedEventArgs e) => SetAutostart(AutostartCheck.IsChecked == true);

        private void WatchdogCheck_Changed(object sender, RoutedEventArgs e) => SetWatchdog(WatchdogCheck.IsChecked == true);

        private void SetAutostart(bool enable)
        {
            _settings.AutostartEnabled = enable;
            var exe = GetOwnExecutablePath();
            var mgr = new AutostartManager();
            if (enable)
            {
                mgr.Enable(exe);
            }
            else
            {
                mgr.Disable();
            }
            _store.Save(_settings);
            AutostartCheck.IsChecked = enable;
        }

        private void SetWatchdog(bool enable)
        {
            _settings.WatchdogEnabled = enable;
            _store.Save(_settings);
            WatchdogCheck.IsChecked = enable;
        }

        // ---------- 日志 ----------

        private void LoadLogTail()
        {
            try
            {
                if (!File.Exists(AppPaths.DshLogPath))
                {
                    return;
                }
                var lines = File.ReadAllLines(AppPaths.DshLogPath);
                var tail = lines.Skip(Math.Max(0, lines.Length - 200));
                LogBox.Text = string.Join(Environment.NewLine, tail);
                LogBox.ScrollToEnd();
            }
            catch { /* best effort */ }
        }

        private void AppendLog(string line)
        {
            LogBox.AppendText(line + Environment.NewLine);
            if (LogBox.Text.Length > 200_000)
            {
                LogBox.Text = LogBox.Text.Substring(LogBox.Text.Length - 100_000);
            }
            LogBox.ScrollToEnd();
        }

        private void ClearLog_Click(object sender, RoutedEventArgs e) => LogBox.Clear();

        private void OpenLogDir_Click(object sender, RoutedEventArgs e) => OpenPath(AppPaths.LogsDir);

        // ---------- 最近会话 ----------

        private void RefreshSessions_Click(object sender, RoutedEventArgs e) => _ = RefreshSessionsAsync();

        private async Task RefreshSessionsAsync()
        {
            SessionList.Items.Clear();
            var scanner = new SessionScanner(AppPaths.SessionsRoot);
            var infos = scanner.Scan(20);

            if (_sessionTitleReader == null || string.IsNullOrEmpty(_settings.NodePath))
            {
                foreach (var info in infos)
                {
                    SessionList.Items.Add(new SessionListItem(info, null));
                }
                return;
            }

            var reader = _sessionTitleReader;
            var metas = await Task.WhenAll(infos.Select(i => Task.Run(() => reader.Read(i.FilePath))));

            for (var i = 0; i < infos.Count; i++)
            {
                SessionList.Items.Add(new SessionListItem(infos[i], metas[i]));
            }
        }

        private void OpenSessionFolder_Click(object sender, RoutedEventArgs e)
        {
            if (SessionList.SelectedItem is SessionListItem item)
            {
                OpenInExplorer(item.Info.FilePath);
            }
        }

        private void OpenSessionFile_Click(object sender, RoutedEventArgs e)
        {
            if (SessionList.SelectedItem is SessionListItem item)
            {
                OpenPath(item.Info.FilePath);
            }
        }

        // ---------- 维护 ----------

        private void Backup_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var bm = new BackupManager(AppPaths.BackupsDir);
                var path = bm.CreateBackup(AppPaths.DshHome);
                BackupHint.Text = "已备份到: " + path;
                _notifier.Notify("备份完成", "配置已备份到 " + path);
            }
            catch (Exception ex)
            {
                BackupHint.Text = "备份失败: " + ex.Message;
            }
        }

        private void Restore_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new OpenFileDialog { Filter = "备份文件 (*.zip)|*.zip" };
            if (dlg.ShowDialog() != true)
            {
                return;
            }

            var result = MessageBox.Show(
                "恢复会覆盖当前 .dsh 配置（恢复前会自动备份一次）。确定继续？",
                "恢复配置", MessageBoxButton.YesNo, MessageBoxImage.Warning);
            if (result != MessageBoxResult.Yes)
            {
                return;
            }

            try
            {
                var bm = new BackupManager(AppPaths.BackupsDir);
                var pre = bm.CreateBackup(AppPaths.DshHome);
                _controller.Stop();
                BackupManager.RestoreTo(dlg.FileName, AppPaths.DshHome);
                BackupHint.Text = "恢复完成（恢复前备份: " + pre + "）";
                _notifier.Notify("恢复完成", "配置已从备份恢复");
            }
            catch (Exception ex)
            {
                BackupHint.Text = "恢复失败: " + ex.Message;
            }
        }

        private async void CheckUpdate_Click(object sender, RoutedEventArgs e)
        {
            VersionText.Text = "检查中…";
            var latest = await GetLatestVersionAsync();
            if (latest == null)
            {
                VersionText.Text = "检查失败（无法访问 npm）。";
                return;
            }

            var current = _settings.DshVersion;
            var state = string.IsNullOrEmpty(current) ? "未知" : (SemVer.IsNewer(latest, current) ? "有更新可用" : "已是最新");
            VersionText.Text = $"已安装: {(string.IsNullOrEmpty(current) ? "未知" : current)}   最新: {latest}   ({state})";
        }

        private async void UpdateDsh_Click(object sender, RoutedEventArgs e)
        {
            VersionText.Text = "更新中…（可能需要一些时间）";
            var ok = await RunNpmAsync("npm install -g @deepseek-ai/dsh@latest");
            if (ok)
            {
                var loc = new DshResolver().Resolve();
                if (loc != null)
                {
                    _settings.NodePath = loc.NodePath;
                    _settings.DshBinPath = loc.BinPath;
                    _settings.DshVersion = loc.Version;
                    _store.Save(_settings);
                }
                VersionText.Text = "更新完成。已安装版本: " + _settings.DshVersion;
                _notifier.Notify("更新完成", "dsh 已更新到 " + _settings.DshVersion);
            }
            else
            {
                VersionText.Text = "更新失败，请查看日志。";
            }
        }

        private void OpenDshDir_Click(object sender, RoutedEventArgs e) => OpenPath(AppPaths.DshHome);

        // ---------- 插件管理 ----------

        private string CliPackageDir()
        {
            var lib = Path.GetDirectoryName(_settings.DshBinPath);
            return lib == null ? "" : (Path.GetDirectoryName(lib) ?? "");
        }

        private void RefreshPlugins_Click(object sender, RoutedEventArgs e) => RefreshPlugins();

        private void RefreshPlugins()
        {
            PluginList.Items.Clear();
            if (_pluginManager == null)
            {
                return;
            }

            foreach (var p in _pluginManager.ListPlugins())
            {
                PluginList.Items.Add(new PluginListItem(p));
            }
        }

        private bool StopHarnessForOperation()
        {
            if (_controller.State == DshState.Running || _controller.State == DshState.Starting)
            {
                _controller.Stop();
                return true;
            }
            return false;
        }

        private async void InstallPlugin_Click(object sender, RoutedEventArgs e)
        {
            var spec = PluginInstallBox.Text.Trim();
            if (string.IsNullOrEmpty(spec))
            {
                PluginHint.Text = "请输入要安装的插件（包名 / name@version / github:repo / file:path）。";
                return;
            }

            var stopped = StopHarnessForOperation();
            PluginHint.Text = "正在安装 " + spec + " …";
            PluginOutput.Text = "";

            var (code, output) = await RunDshPluginAsync(new[] { "add", spec });
            PluginOutput.Text = output;
            PluginHint.Text = (code == 0 ? "安装完成" : "安装失败 (exit " + code + ")")
                + (stopped ? "；harness 已停止，请手动重新启动。" : "");
            RefreshPlugins();
            PluginInstallBox.Clear();
        }

        private async void UninstallPlugin_Click(object sender, RoutedEventArgs e)
        {
            if (PluginList.SelectedItem is not PluginListItem item)
            {
                PluginHint.Text = "请先在列表里选中要卸载的插件。";
                return;
            }
            if (item.Info.Status != PluginStatus.Managed && item.Info.Status != PluginStatus.Inactive)
            {
                PluginHint.Text = "只能卸载「插件」或「未加载」项；内置/残留请用其它操作。";
                return;
            }

            var r = MessageBox.Show("确定卸载插件 " + item.Info.Name + " ？", "卸载插件", MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (r != MessageBoxResult.Yes)
            {
                return;
            }

            var stopped = StopHarnessForOperation();
            PluginHint.Text = "正在卸载 " + item.Info.Name + " …";
            PluginOutput.Text = "";

            var (code, output) = await RunDshPluginAsync(new[] { "remove", item.Info.Name });
            PluginOutput.Text = output;
            PluginHint.Text = (code == 0 ? "卸载完成" : "卸载失败 (exit " + code + ")")
                + (stopped ? "；harness 已停止，请手动重新启动。" : "");
            RefreshPlugins();
        }

        private async void ReinstallDeps_Click(object sender, RoutedEventArgs e)
        {
            var stopped = StopHarnessForOperation();
            PluginHint.Text = "正在重装依赖 (pnpm install) …";
            PluginOutput.Text = "";

            var (code, output) = await RunDshPluginAsync(new[] { "install" });
            PluginOutput.Text = output;
            PluginHint.Text = (code == 0 ? "重装完成" : "重装失败 (exit " + code + ")")
                + (stopped ? "；harness 已停止，请手动重新启动。" : "");
            RefreshPlugins();
        }

        private void ScanLeftovers_Click(object sender, RoutedEventArgs e)
        {
            if (_pluginManager == null)
            {
                return;
            }

            var leftovers = _pluginManager.ScanLeftovers();
            if (leftovers.Count == 0)
            {
                PluginOutput.Text = "未发现残留。";
                PluginHint.Text = "";
            }
            else
            {
                PluginOutput.Text = "发现 " + leftovers.Count + " 个残留 bundle 条目（已卸载但仍在 bundles 列表）：\n" + string.Join("\n", leftovers);
                PluginHint.Text = "点击「清理残留」可移除这些条目并重装依赖。";
            }
        }

        private async void CleanLeftovers_Click(object sender, RoutedEventArgs e)
        {
            if (_pluginManager == null)
            {
                return;
            }

            var leftovers = _pluginManager.ScanLeftovers();
            if (leftovers.Count == 0)
            {
                PluginOutput.Text = "未发现残留，无需清理。";
                return;
            }

            var r = MessageBox.Show(
                "将移除 " + leftovers.Count + " 个残留 bundle 条目并重装依赖：\n" + string.Join("\n", leftovers) + "\n\n确定继续？",
                "清理残留", MessageBoxButton.YesNo, MessageBoxImage.Warning);
            if (r != MessageBoxResult.Yes)
            {
                return;
            }

            var stopped = StopHarnessForOperation();
            _pluginManager.RemoveStaleBundles(leftovers);
            PluginHint.Text = "正在重装依赖以完成清理 …";
            PluginOutput.Text = "";

            var (code, output) = await RunDshPluginAsync(new[] { "install" });
            PluginOutput.Text = "已移除残留条目：\n" + string.Join("\n", leftovers) + "\n\n" + output;
            PluginHint.Text = (code == 0 ? "清理完成" : "清理完成，但重装失败 (exit " + code + ")")
                + (stopped ? "；harness 已停止，请手动重新启动。" : "");
            RefreshPlugins();
        }

        private Task<(int exitCode, string output)> RunDshPluginAsync(string[] pnpmArgs)
        {
            var args = new[] { "plugin", "--profile", AppPaths.ProfileName }.Concat(pnpmArgs).ToArray();
            return Task.Run(() => RunNodeScriptBlocking(_settings.DshBinPath, args));
        }

        /// <summary>Runs `node &lt;scriptPath&gt; &lt;args...&gt;` hidden and captures all output.</summary>
        private (int exitCode, string output) RunNodeScriptBlocking(string scriptPath, string[] args)
        {
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = _settings.NodePath,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    StandardOutputEncoding = Encoding.UTF8,
                    StandardErrorEncoding = Encoding.UTF8,
                    WorkingDirectory = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)
                };
                psi.ArgumentList.Add(scriptPath);
                foreach (var a in args)
                {
                    psi.ArgumentList.Add(a);
                }

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
            catch (Exception ex)
            {
                return (1, "执行失败: " + ex.Message);
            }
        }

        // ---------- 一键自动修复 ----------

        private void RepairButton_Click(object sender, RoutedEventArgs e) => OneClickRepair();

        private async void OneClickRepair()
        {
            if (_repairRunning)
            {
                return;
            }
            _repairRunning = true;
            try
            {
                var stopped = StopHarnessForOperation();
                MaintenanceTab.IsSelected = true;
                RepairHint.Text = "修复中…";
                RepairOutput.Text = "";

                var cliPath = Path.Combine(AppPaths.ProfileDir, "node_modules", "dsh-win32", "bin", "cli.mjs");
                var runner = new RepairRunner(
                    _settings, _store, _pluginManager!, cliPath, RunNodeScriptBlocking,
                    line => Dispatcher.Invoke(() => AppendRepairLine(line)));

                var report = await Task.Run(() => runner.Run());

                AppendRepairLine(FormatRepairReport(report));
                RepairHint.Text = (report.AllSucceeded ? "修复完成 ✔" : "修复完成（部分步骤失败）⚠")
                    + (stopped ? "；harness 已停止，请手动重新启动。" : "");
                _notifier.Notify(
                    report.AllSucceeded ? "一键修复完成" : "一键修复完成（部分失败）",
                    report.AllSucceeded ? "所有步骤均已通过。" : "请打开主界面查看修复明细。");
                RefreshPlugins();
                UpdateVersionText();
            }
            finally
            {
                _repairRunning = false;
            }
        }

        private void AppendRepairLine(string line)
        {
            RepairOutput.AppendText(line + Environment.NewLine);
            if (RepairOutput.Text.Length > 100_000)
            {
                RepairOutput.Text = RepairOutput.Text.Substring(RepairOutput.Text.Length - 50_000);
            }
            RepairOutput.ScrollToEnd();
        }

        private static string FormatRepairReport(RepairReport report)
        {
            var sb = new StringBuilder();
            sb.AppendLine(report.AllSucceeded ? "✅ 修复完成，所有步骤成功" : "⚠ 修复完成，部分步骤失败");
            foreach (var s in report.Steps)
            {
                sb.AppendLine((s.Succeeded ? "  ✔ " : "  ✘ ") + s.StepName + " — " + s.Detail);
            }
            return sb.ToString().TrimEnd();
        }

        private void UpdateVersionText()
        {
            var v = string.IsNullOrEmpty(_settings.DshVersion) ? "未知" : _settings.DshVersion;
            VersionText.Text = "已安装 dsh 版本: " + v;
        }

        private Task<string?> GetLatestVersionAsync()
        {
            return Task.Run(() =>
            {
                try
                {
                    var (_, output) = RunCmd("npm view @deepseek-ai/dsh version");
                    return output.Trim().Split('\n')
                        .Select(x => x.Trim())
                        .FirstOrDefault(x => x.Length > 0);
                }
                catch
                {
                    return null;
                }
            });
        }

        private Task<bool> RunNpmAsync(string npmCommand)
        {
            return Task.Run(() =>
            {
                try
                {
                    var (code, output) = RunCmd(npmCommand);
                    Dispatcher.Invoke(() => AppendLog(output));
                    return code == 0;
                }
                catch (Exception ex)
                {
                    Dispatcher.Invoke(() => AppendLog("npm 执行失败: " + ex.Message));
                    return false;
                }
            });
        }

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
            var sb = new System.Text.StringBuilder();
            p.OutputDataReceived += (_, e) => { if (e.Data != null) { lock (sb) { sb.AppendLine(e.Data); } } };
            p.ErrorDataReceived += (_, e) => { if (e.Data != null) { lock (sb) { sb.AppendLine(e.Data); } } };
            p.Start();
            p.BeginOutputReadLine();
            p.BeginErrorReadLine();
            p.WaitForExit();
            lock (sb) { return (p.ExitCode, sb.ToString()); }
        }

        // ---------- 托盘 / 退出 ----------

        public void HideToTray()
        {
            Hide();
            if (!_trayHintShown)
            {
                _trayHintShown = true;
                _notifier.Notify("仍在后台运行", "DeepSeek Harness 启动器已最小化到系统托盘，双击图标可重新打开。");
            }
        }

        public void ShowFromTray()
        {
            Show();
            WindowState = WindowState.Normal;
            Activate();
        }

        protected override void OnClosing(CancelEventArgs e)
        {
            if (_allowClose)
            {
                base.OnClosing(e);
                return;
            }
            e.Cancel = true;
            HideToTray();
        }

        protected override void OnStateChanged(EventArgs e)
        {
            base.OnStateChanged(e);
            if (WindowState == WindowState.Minimized && _settings.MinimizeToTray)
            {
                HideToTray();
            }
        }

        private void ExitApplication()
        {
            if (_controller.State == DshState.Running || _controller.State == DshState.Starting)
            {
                var r = MessageBox.Show(
                    "DeepSeek Harness 正在运行，退出启动器时会同时停止它。确定退出？",
                    "退出", MessageBoxButton.YesNo, MessageBoxImage.Question);
                if (r != MessageBoxResult.Yes)
                {
                    return;
                }
            }

            _allowClose = true;
            _controller.Dispose();
            _trayIcon.Dispose();
            Application.Current.Shutdown();
        }

        // ---------- 通用 ----------

        private static void OpenPath(string path)
        {
            try
            {
                Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
            }
            catch (Exception ex)
            {
                MessageBox.Show("打开失败: " + ex.Message, "DSH Launcher", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private static void OpenInExplorer(string path)
        {
            try
            {
                Process.Start(new ProcessStartInfo("explorer.exe", "/select,\"" + path + "\"") { UseShellExecute = false });
            }
            catch (Exception ex)
            {
                MessageBox.Show("打开失败: " + ex.Message, "DSH Launcher", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private static string GetOwnExecutablePath()
        {
            try
            {
                return Process.GetCurrentProcess().MainModule?.FileName
                    ?? System.Reflection.Assembly.GetExecutingAssembly().Location;
            }
            catch
            {
                return System.Reflection.Assembly.GetExecutingAssembly().Location;
            }
        }

        private sealed class SessionListItem
        {
            public SessionInfo Info { get; }
            public SessionMeta? Meta { get; }

            public SessionListItem(SessionInfo info, SessionMeta? meta)
            {
                Info = info;
                Meta = meta;
            }

            public override string ToString()
            {
                var time = Meta?.CreatedAt != null
                    ? DateTimeOffset.FromUnixTimeMilliseconds(Meta.CreatedAt.Value).ToLocalTime().ToString("MM-dd HH:mm")
                    : Info.LastWriteTimeUtc.ToLocalTime().ToString("MM-dd HH:mm");

                string title;
                if (Meta == null)
                {
                    title = "(读取失败)";
                }
                else if (string.IsNullOrWhiteSpace(Meta.Title))
                {
                    title = "(空会话)" + (string.IsNullOrWhiteSpace(Meta.AgentPreset) ? "" : " · " + Meta.AgentPreset);
                }
                else
                {
                    title = Meta.Title;
                }

                var cwd = string.IsNullOrWhiteSpace(Meta?.Cwd) ? "" : " —— " + Meta.Cwd;
                return time + "  " + title + cwd;
            }
        }

        private sealed class PluginListItem
        {
            public PluginInfo Info { get; }

            public PluginListItem(PluginInfo info)
            {
                Info = info;
            }

            public override string ToString()
            {
                var tag = Info.Status switch
                {
                    PluginStatus.InBox => "[内置]",
                    PluginStatus.Managed => "[插件]",
                    PluginStatus.Stale => "[残留]",
                    PluginStatus.Inactive => "[未加载]",
                    _ => ""
                };
                return tag + "  " + Info.Name + "  (" + Info.Version + ")";
            }
        }
    }
}
