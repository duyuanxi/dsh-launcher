using System;
using System.Diagnostics;
using System.IO;
using System.Timers;

namespace DshLauncher.Services
{
    public enum DshState
    {
        Stopped,
        Starting,
        Running,
        Error
    }

    /// <summary>
    /// Coordinates the dsh child process: start/stop/restart, liveness polling,
    /// URL resolution, and the crash-restart watchdog. Events fire from thread
    /// pool threads; the UI layer marshals them onto the dispatcher.
    /// </summary>
    public class DshController : IDisposable
    {
        private readonly DshProcessManager _manager;
        private readonly AppSettings _settings;
        private readonly Timer _poll;
        private Timer? _restartTimer;
        private int _restartAttempts;
        private int? _adoptedPid;

        public event Action<DshState>? StateChanged;
        public event Action<string>? LogLine;
        public event Action<string>? ResolvedUrlChanged;

        public DshState State { get; private set; } = DshState.Stopped;
        public string? ResolvedUrl { get; private set; }

        public DshController(DshProcessManager manager, AppSettings settings)
        {
            _manager = manager;
            _settings = settings;

            _manager.OutputReceived += OnOutput;
            _manager.Exited += OnExited;

            _poll = new Timer(2000) { AutoReset = true };
            _poll.Elapsed += (_, __) => Poll();
        }

        public void Start()
        {
            _restartAttempts = 0;

            if (_settings.Port != 0)
            {
                var detection = HarnessDetector.Detect(_settings.Host, _settings.Port);
                if (detection.Status == HarnessPortStatus.HarnessRunning)
                {
                    _adoptedPid = detection.OwnerPid;
                    ResolvedUrl = DshCommandBuilder.BuildUrl(_settings.Host, _settings.Port);
                    ResolvedUrlChanged?.Invoke(ResolvedUrl);
                    Log($"检测到 harness 已在运行（PID {_adoptedPid}），直接接管状态。");
                    SetState(DshState.Running);
                    _poll.Start();
                    return;
                }
                if (detection.Status == HarnessPortStatus.ForeignOccupied)
                {
                    Log($"端口 {_settings.Host}:{_settings.Port} 被其它程序占用（非 harness），未启动。");
                    SetState(DshState.Error);
                    return;
                }
            }

            _adoptedPid = null;
            SetState(DshState.Starting);
            ResolvedUrl = _settings.Port == 0 ? null : DshCommandBuilder.BuildUrl(_settings.Host, _settings.Port);
            ResolvedUrlChanged?.Invoke(ResolvedUrl ?? "");

            try
            {
                _manager.Start(_settings.Port, _settings.Host, dshHome: AppPaths.DshHome);
                _poll.Start();
            }
            catch (Exception ex)
            {
                SetState(DshState.Error);
                Log("启动失败: " + ex.Message);
            }
        }

        public void Stop()
        {
            _restartTimer?.Stop();
            _restartTimer?.Dispose();
            _restartTimer = null;

            _poll.Stop();
            _manager.Stop();

            if (_adoptedPid != null)
            {
                KillProcessTree(_adoptedPid.Value);
                _adoptedPid = null;
            }

            SetState(DshState.Stopped);
        }

        public void Restart()
        {
            Stop();
            Start();
        }

        private void OnOutput(string line)
        {
            Log(line);

            if (_settings.Port == 0)
            {
                var url = HealthChecker.ParseWebUrl(line);
                if (url != null && url != ResolvedUrl)
                {
                    ResolvedUrl = url;
                    ResolvedUrlChanged?.Invoke(url);
                }
            }
        }

        private void OnExited(int code, bool wasStopRequested)
        {
            if (wasStopRequested)
            {
                return;
            }
            if (_manager.IsRunning)
            {
                return; // stale event from a previous process
            }
            if (_restartTimer != null)
            {
                return; // a restart is already scheduled
            }

            SetState(DshState.Error);

            if (!_settings.WatchdogEnabled)
            {
                _poll.Stop();
                Log($"harness 已退出 (exit {code})");
                return;
            }

            var delay = Backoff.Next(_restartAttempts, TimeSpan.FromSeconds(3), TimeSpan.FromSeconds(60));
            _restartAttempts++;
            Log($"harness 已退出 (exit {code})，{delay.TotalSeconds:0}s 后自动重启");

            _restartTimer = new Timer(delay.TotalMilliseconds) { AutoReset = false };
            _restartTimer.Elapsed += (_, __) =>
            {
                _restartTimer?.Dispose();
                _restartTimer = null;
                Start();
            };
            _restartTimer.Start();
        }

        private void Poll()
        {
            if (ResolvedUrl == null)
            {
                return; // dynamic port not yet printed by dsh
            }

            var uri = new Uri(ResolvedUrl);
            var open = HealthChecker.IsPortOpen(uri.Host, uri.Port, 1000);

            if (State == DshState.Starting && open)
            {
                SetState(DshState.Running);
            }
            else if (State == DshState.Running && !open && !_manager.IsRunning)
            {
                // Safety net: process gone and port down without an Exited event.
                OnExited(-1, false);
            }
        }

        private void Log(string line)
        {
            try
            {
                Directory.CreateDirectory(AppPaths.LogsDir);
                File.AppendAllText(
                    AppPaths.LauncherLogPath,
                    DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff") + "  " + line + Environment.NewLine);
            }
            catch { /* best effort */ }

            LogLine?.Invoke(line);
        }

        private static void KillProcessTree(int pid)
        {
            try
            {
                using var p = Process.GetProcessById(pid);
                p.Kill(entireProcessTree: true);
            }
            catch { /* already gone */ }
        }

        private void SetState(DshState state)
        {
            if (State == state)
            {
                return;
            }
            State = state;
            StateChanged?.Invoke(state);
        }

        public void Dispose()
        {
            _restartTimer?.Stop();
            _restartTimer?.Dispose();
            _restartTimer = null;
            _poll.Stop();
            _poll.Dispose();
            _manager.Dispose();
        }
    }
}
