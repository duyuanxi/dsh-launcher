using System;
using System.Diagnostics;
using System.IO;

namespace DshLauncher.Services
{
    /// <summary>
    /// Owns the dsh web child process: spawns it hidden with stdout/stderr
    /// redirected to a log, and kills the whole process tree on stop.
    /// </summary>
    public class DshProcessManager : IDisposable
    {
        private readonly string _nodePath;
        private readonly string _dshBinPath;
        private readonly string _logPath;
        private Process? _process;
        private bool _stopRequested;

        /// <summary>A line of dsh stdout/stderr.</summary>
        public event Action<string>? OutputReceived;

        /// <summary>Raised when the child exits: (exitCode, wasStopRequested).</summary>
        public event Action<int, bool>? Exited;

        public DshProcessManager(string nodePath, string dshBinPath, string logPath)
        {
            _nodePath = nodePath;
            _dshBinPath = dshBinPath;
            _logPath = logPath;
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

            var psi = new ProcessStartInfo
            {
                FileName = _nodePath,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                StandardOutputEncoding = System.Text.Encoding.UTF8,
                StandardErrorEncoding = System.Text.Encoding.UTF8,
                WorkingDirectory = workingDir ?? Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)
            };

            // argv = node <bin.js> web --port N --host H --no-open
            psi.ArgumentList.Add(_dshBinPath);
            foreach (var arg in DshCommandBuilder.BuildArguments(port, host))
            {
                psi.ArgumentList.Add(arg);
            }

            if (!string.IsNullOrEmpty(dshHome))
            {
                psi.Environment["DSH_HOME"] = dshHome;
            }

            var p = new Process { StartInfo = psi, EnableRaisingEvents = true };
            p.OutputDataReceived += (_, e) => { if (e.Data != null) HandleLine(e.Data); };
            p.ErrorDataReceived += (_, e) => { if (e.Data != null) HandleLine(e.Data); };
            p.Exited += (_, __) =>
            {
                var code = -1;
                try { code = p.ExitCode; } catch { /* process not fully released yet */ }
                Exited?.Invoke(code, _stopRequested);
            };

            p.Start();
            p.BeginOutputReadLine();
            p.BeginErrorReadLine();
            _process = p;
        }

        public void Stop()
        {
            _stopRequested = true;
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

        private void HandleLine(string line)
        {
            try
            {
                File.AppendAllText(_logPath, DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff") + "  " + line + Environment.NewLine);
            }
            catch { /* log write is best-effort */ }

            OutputReceived?.Invoke(line);
        }

        public void Dispose() => Stop();
    }
}
