using System;
using System.IO;
using System.Threading;
using System.Windows;
using DshLauncher.Services;

namespace DshLauncher
{
    public partial class App : Application
    {
        private Mutex? _mutex;
        private EventWaitHandle? _showEvent;
        private DshController? _controller;
        private MainWindow? _mainWindow;

        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            var autostart = Array.IndexOf(e.Args, "--autostart") >= 0;
            var startNow = Array.IndexOf(e.Args, "--start") >= 0;
            var forceInstall = Array.IndexOf(e.Args, "--install") >= 0;

            // First run without any mode flag (e.g. a freshly downloaded setup
            // exe) opens the one-click installer instead of the launcher.
            if (forceInstall || (!autostart && !startNow && !InstallerService.IsInstalled()))
            {
                var installer = new InstallerWindow();
                installer.Closed += (_, __) =>
                {
                    if (installer.RunPortableRequested)
                    {
                        StartLauncher(false, false);
                    }
                    else
                    {
                        Shutdown();
                    }
                };
                MainWindow = installer;
                installer.Show();
                return;
            }

            StartLauncher(autostart, startNow);
        }

        private void StartLauncher(bool autostart, bool startNow)
        {
            var identity = Environment.UserName.Replace('\\', '_');

            _mutex = new Mutex(true, "DshLauncher.SingleInstance." + identity, out var createdNew);
            if (!createdNew)
            {
                // Signal the running instance to surface its window, then exit.
                try
                {
                    using var ev = EventWaitHandle.OpenExisting("DshLauncher.ShowRequested." + identity);
                    ev.Set();
                }
                catch { /* first instance may not have created the event yet */ }

                _mutex.Dispose();
                _mutex = null;
                Shutdown();
                return;
            }

            try
            {
                SessionHelper.EnsureExtracted();
                _showEvent = new EventWaitHandle(false, EventResetMode.AutoReset, "DshLauncher.ShowRequested." + identity);
                var waitThread = new Thread(() =>
                {
                    while (_showEvent != null && _showEvent.WaitOne())
                    {
                        Dispatcher.Invoke(() => _mainWindow?.ShowFromTray());
                    }
                })
                {
                    IsBackground = true
                };
                waitThread.Start();

                var store = new SettingsStore(SettingsStore.DefaultFilePath);
                var settings = store.Load();
                ResolveDshIfNeeded(settings, store);

                var manager = new DshProcessManager(settings.NodePath, settings.DshBinPath, AppPaths.DshLogPath);
                _controller = new DshController(manager, settings);

                _mainWindow = new MainWindow(_controller, settings, store);
                MainWindow = _mainWindow;

                if (autostart && settings.AutostartSilent)
                {
                    _mainWindow.HideToTray();
                    ScheduleAutostart(settings);
                }
                else
                {
                    _mainWindow.Show();
                    if (startNow)
                    {
                        _mainWindow.StartFromLaunch();
                    }
                }
            }
            catch (Exception ex)
            {
                Log("启动器初始化失败: " + ex);
                MessageBox.Show("启动器初始化失败:\n" + ex.Message, "DSH Launcher", MessageBoxButton.OK, MessageBoxImage.Error);
                Shutdown();
            }
        }

        private static void ResolveDshIfNeeded(AppSettings settings, SettingsStore store)
        {
            if (!string.IsNullOrEmpty(settings.NodePath) && !string.IsNullOrEmpty(settings.DshBinPath))
            {
                return;
            }

            var loc = new DshResolver().Resolve();
            if (loc == null)
            {
                return;
            }

            settings.NodePath = loc.NodePath;
            settings.DshBinPath = loc.BinPath;
            settings.DshVersion = loc.Version;
            store.Save(settings);
        }

        private void ScheduleAutostart(AppSettings settings)
        {
            var delay = Math.Max(0, settings.AutostartDelaySeconds);
            if (delay > 0)
            {
                var timer = new System.Threading.Timer(_ => _controller?.Start(), null, delay * 1000, Timeout.Infinite);
                GC.KeepAlive(timer);
            }
            else
            {
                _controller?.Start();
            }
        }

        private static void Log(string message)
        {
            try
            {
                Directory.CreateDirectory(AppPaths.LogsDir);
                File.AppendAllText(
                    AppPaths.LauncherLogPath,
                    DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff") + "  " + message + Environment.NewLine);
            }
            catch { /* best effort */ }
        }

        protected override void OnExit(ExitEventArgs e)
        {
            _showEvent?.Dispose();
            _showEvent = null;
            _mutex?.ReleaseMutex();
            _mutex?.Dispose();
            _mutex = null;
            base.OnExit(e);
        }
    }
}
