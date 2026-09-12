using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Timers;

namespace DshLauncher.Services
{
    /// <summary>
    /// Owns the dsh web child process. The child's stdout/stderr is redirected
    /// to the log FILE (not a pipe) so the harness survives launcher exit and
    /// keeps running independently; the launcher tails the file for its live
    /// log view. Stop kills the whole process tree.
    /// </summary>
    public class DshProcessManager : IDisposable
    {
        private readonly string _nodePath;
        private readonly string _dshBinPath;
        private readonly string _logPath;
        private Process? _process;
        private bool _stopRequested;
        private readonly Timer _tailTimer;
        private long _tailPosition;

        /// <summary>A line of dsh stdout/stderr (fed by the log tailer).</summary>
        public event Action<string>? OutputReceived;

        /// <summary>Raised when the child exits: (exitCode, wasStopRequested).</summary>
        public event Action<int, bool>? Exited;

        public DshProcessManager(string nodePath, string dshBinPath, string logPath)
        {
            _nodePath = nodePath;
            _dshBinPath = dshBinPath;
            _logPath = logPath;
            _tailTimer = new Timer(500) { AutoReset = true };
            _tailTimer.Elapsed += (_, __) => Tail();
        }

        public bool IsRunning => _process != null && !_process.HasExited;

        public void Start(int port, string host, string? workingDir = null, string? dshHome = null)
        {
            Stop();
            _stopRequested = false;

            var logDir = Path.GetDirectoryName(_logPath);
            if (!string.IsNullOrEmpty(logDir))
            {
                Directory.CreateDirectory(logDir);
            }
            _tailPosition = File.Exists(_logPath) ? new FileInfo(_logPath).Length : 0;

            // 启动命令写入 .cmd 脚本再执行：避免 cmd /c 的 argv 引号剥离问题，
            // 且子进程的 stdout/stderr 由脚本重定向到日志文件，launcher 退出后
            // harness 独立存活。
            var scriptDir = logDir ?? Path.GetTempPath();
            var scriptPath = Path.Combine(scriptDir, "start-dsh.cmd");
            File.WriteAllText(scriptPath, BuildStartScript(port, host, dshHome), Encoding.ASCII);

            var psi = new ProcessStartInfo
            {
                FileName = "cmd.exe",
                UseShellExecute = false,
                CreateNoWindow = true,
                WorkingDirectory = workingDir ?? Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)
            };
            psi.ArgumentList.Add("/c");
            psi.ArgumentList.Add(scriptPath);

            var p = new Process { StartInfo = psi, EnableRaisingEvents = true };
            p.Exited += (_, __) =>
            {
                Tail();
                _tailTimer.Stop();
                var code = -1;
                try { code = p.ExitCode; } catch { /* process not fully released yet */ }
                Exited?.Invoke(code, _stopRequested);
            };

            p.Start();
            _process = p;
            _tailTimer.Start();
        }

        /// <summary>
        /// Content of the start script: sets DSH_HOME, then runs
        /// <c>node "bin.js" web --port N --host H --no-open &gt;&gt; "log" 2&gt;&amp;1</c>
        /// so the child owns the log file independently of the launcher.
        /// </summary>
        internal static string BuildStartScript(string nodePath, string binPath, string logPath, int port, string host, string? dshHome)
        {
            var sb = new StringBuilder();
            sb.Append("@echo off\r\n");
            if (!string.IsNullOrEmpty(dshHome))
            {
                sb.Append("set \"DSH_HOME=").Append(dshHome).Append("\"\r\n");
            }
            sb.Append('"').Append(nodePath).Append("\" \"");
            sb.Append(binPath).Append("\" ");
            foreach (var arg in DshCommandBuilder.BuildArguments(port, host))
            {
                sb.Append(arg).Append(' ');
            }
            sb.Append(">> \"").Append(logPath).Append("\" 2>&1\r\n");
            return sb.ToString();
        }

        private string BuildStartScript(int port, string host, string? dshHome)
        {
            return BuildStartScript(_nodePath, _dshBinPath, _logPath, port, host, dshHome);
        }

        private void Tail()
        {
            try
            {
                if (!File.Exists(_logPath))
                {
                    return;
                }

                long length;
                using (var fs = new FileStream(_logPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
                {
                    length = fs.Length;
                    if (length <= _tailPosition)
                    {
                        return;
                    }

                    fs.Seek(_tailPosition, SeekOrigin.Begin);
                    var count = (int)Math.Min(length - _tailPosition, 1024 * 1024);
                    var buffer = new byte[count];
                    var read = fs.Read(buffer, 0, count);
                    _tailPosition += read;
                    var text = Encoding.UTF8.GetString(buffer, 0, read);
                    foreach (var line in text.Split('\n'))
                    {
                        var t = line.TrimEnd('\r');
                        if (t.Length > 0)
                        {
                            OutputReceived?.Invoke(t);
                        }
                    }
                }
            }
            catch { /* best effort */ }
        }

        public void Stop()
        {
            _stopRequested = true;
            _tailTimer.Stop();
            if (_process == null)
            {
                return;
            }

            try
            {
                if (!_process.HasExited)
                {
                    _process.Kill(entireProcessTree: true);
                    _process.WaitForExit(5000);
                }
            }
            catch { /* already exited or no permission */ }

            try { _process.Dispose(); } catch { /* ignore */ }
            _process = null;
        }

        public void Dispose()
        {
            _tailTimer.Stop();
            _tailTimer.Dispose();
            Stop();
        }
    }
}
