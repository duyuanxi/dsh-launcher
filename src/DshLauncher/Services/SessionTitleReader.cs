using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;

namespace DshLauncher.Services
{
    public class SessionMeta
    {
        public string? Id { get; set; }
        public long? CreatedAt { get; set; }
        public string? Cwd { get; set; }
        public string? AgentPreset { get; set; }
        public string? Title { get; set; }
    }

    /// <summary>
    /// Reads a session's metadata and human-readable title by shelling out to
    /// the bundled node helper (session-reader.mjs), which decompresses the
    /// zstd log with node:zlib and extracts title events.
    /// </summary>
    public class SessionTitleReader
    {
        private readonly string _nodePath;
        private readonly string _helperPath;

        public SessionTitleReader(string nodePath, string helperPath)
        {
            _nodePath = nodePath;
            _helperPath = helperPath;
        }

        public SessionMeta? Read(string sessionFilePath)
        {
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = _nodePath,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    StandardOutputEncoding = Encoding.UTF8,
                    StandardErrorEncoding = Encoding.UTF8
                };
                psi.ArgumentList.Add(_helperPath);
                psi.ArgumentList.Add(sessionFilePath);

                using var p = Process.Start(psi);
                if (p == null)
                {
                    return null;
                }

                var output = p.StandardOutput.ReadToEnd();
                p.WaitForExit();
                if (p.ExitCode != 0)
                {
                    return null;
                }

                var line = output.Split('\n').Select(x => x.Trim()).LastOrDefault(x => x.Length > 0);
                if (line == null)
                {
                    return null;
                }

                return JsonSerializer.Deserialize<SessionMeta>(
                    line, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            }
            catch
            {
                return null;
            }
        }
    }
}
