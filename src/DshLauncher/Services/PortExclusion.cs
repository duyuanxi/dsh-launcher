using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text.RegularExpressions;

namespace DshLauncher.Services
{
    public enum PortExclusionStatus
    {
        None,
        AdminExcluded,  // fixed: port removed from winnat's pool (bindable)
        SystemReserved  // problem: port inside a dynamic/system reserved range (bind fails with EACCES)
    }

    public readonly struct PortRange
    {
        public int Start { get; }
        public int End { get; }
        public bool IsAdminExclusion { get; }

        public PortRange(int start, int end, bool isAdminExclusion)
        {
            Start = start;
            End = end;
            IsAdminExclusion = isAdminExclusion;
        }

        public override string ToString() => Start + "-" + End;
    }

    /// <summary>
    /// Detects whether a TCP port falls inside a Windows reserved port range
    /// (winnat / Hyper-V / WSL2 dynamic exclusions), which makes bind fail with
    /// EACCES even when nothing is listening.
    /// </summary>
    public static class PortExclusionService
    {
        private static readonly Regex RangeRegex = new Regex(@"^\s*(\d+)\s+(\d+)\s*(\*?)\s*$", RegexOptions.Compiled);

        public static IReadOnlyList<PortRange> Parse(string netshOutput)
        {
            var ranges = new List<PortRange>();
            foreach (var line in netshOutput.Split('\n'))
            {
                var m = RangeRegex.Match(line);
                if (!m.Success)
                {
                    continue;
                }
                ranges.Add(new PortRange(
                    int.Parse(m.Groups[1].Value),
                    int.Parse(m.Groups[2].Value),
                    m.Groups[3].Length > 0));
            }
            return ranges;
        }

        public static PortExclusionStatus Classify(int port, string netshOutput)
        {
            var admin = false;
            var system = false;
            foreach (var r in Parse(netshOutput))
            {
                if (port < r.Start || port > r.End)
                {
                    continue;
                }
                if (r.IsAdminExclusion)
                {
                    admin = true;
                }
                else
                {
                    system = true;
                }
            }

            // an explicit admin exclusion wins over any overlapping system range
            if (admin)
            {
                return PortExclusionStatus.AdminExcluded;
            }
            return system ? PortExclusionStatus.SystemReserved : PortExclusionStatus.None;
        }

        /// <summary>Runs `netsh int ipv4 show excludedportrange protocol=tcp` and classifies the port.</summary>
        public static PortExclusionStatus Check(int port)
        {
            try
            {
                return Classify(port, RunNetsh());
            }
            catch
            {
                return PortExclusionStatus.None;
            }
        }

        private static string RunNetsh()
        {
            var psi = new ProcessStartInfo
            {
                FileName = "netsh.exe",
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };
            psi.ArgumentList.Add("int");
            psi.ArgumentList.Add("ipv4");
            psi.ArgumentList.Add("show");
            psi.ArgumentList.Add("excludedportrange");
            psi.ArgumentList.Add("protocol=tcp");

            using var p = Process.Start(psi);
            if (p == null)
            {
                return string.Empty;
            }
            var output = p.StandardOutput.ReadToEnd();
            p.WaitForExit();
            return output;
        }
    }

    /// <summary>
    /// Permanently removes a port from winnat's dynamic reservation pool by
    /// adding an admin exclusion (requires elevation; a UAC prompt appears).
    /// </summary>
    public static class PortExclusionFixer
    {
        public static string BuildScript(int port)
        {
            return "@echo off\r\n" +
                "net stop winnat\r\n" +
                "netsh int ipv4 add excludedportrange protocol=tcp startport=" + port + " numberofports=1 store=persistent\r\n" +
                "net start winnat\r\n";
        }

        /// <summary>Writes and runs the fix script elevated. Returns false when the user cancels UAC or the script fails.</summary>
        public static bool Run(int port, string? scriptPath = null)
        {
            var path = scriptPath ?? Path.Combine(Path.GetTempPath(), "dsh-port-fix-" + port + ".cmd");
            File.WriteAllText(path, BuildScript(port));
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = path,
                    UseShellExecute = true,
                    Verb = "RunAs",
                    CreateNoWindow = false
                };
                using var p = Process.Start(psi);
                p?.WaitForExit();
                return p?.ExitCode == 0;
            }
            catch (System.ComponentModel.Win32Exception)
            {
                return false; // user cancelled the UAC prompt
            }
        }
    }
}
