using System;
using System.Net.Sockets;

namespace DshLauncher.Services
{
    /// <summary>
    /// Liveness checks for the running dsh web server.
    /// </summary>
    public static class HealthChecker
    {
        public static bool IsPortOpen(string host, int port, int timeoutMs = 2000)
        {
            try
            {
                using var client = new TcpClient();
                var connect = client.ConnectAsync(host, port);
                if (!connect.Wait(timeoutMs))
                {
                    return false;
                }
                return client.Connected;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Extracts the first <c>http://...</c> URL from a dsh startup line such as
        /// <c>dsh web: http://127.0.0.1:3080 (LAN: http://192.168.1.5:3080)</c>.
        /// </summary>
        public static string? ParseWebUrl(string line)
        {
            const string marker = "http://";
            var idx = line.IndexOf(marker, StringComparison.Ordinal);
            if (idx < 0)
            {
                return null;
            }

            var end = idx + marker.Length;
            while (end < line.Length && !char.IsWhiteSpace(line[end]) && line[end] != ')')
            {
                end++;
            }
            return line.Substring(idx, end - idx);
        }
    }
}
