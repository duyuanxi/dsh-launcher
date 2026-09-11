using System;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Text;

namespace DshLauncher.Services
{
    public enum HarnessPortStatus
    {
        NotListening,
        HarnessRunning,
        ForeignOccupied
    }

    public readonly struct HarnessDetection
    {
        public HarnessPortStatus Status { get; }
        public int? OwnerPid { get; }

        public HarnessDetection(HarnessPortStatus status, int? ownerPid)
        {
            Status = status;
            OwnerPid = ownerPid;
        }
    }

    /// <summary>
    /// Determines whether a port is occupied by a running DeepSeek Harness
    /// (a node process serving HTTP) rather than by some other program.
    /// </summary>
    public static class HarnessDetector
    {
        public static HarnessDetection Detect(string host, int port)
        {
            if (!HealthChecker.IsPortOpen(host, port, 800))
            {
                return new HarnessDetection(HarnessPortStatus.NotListening, null);
            }

            var pid = FindOwnerPid(host, port);
            var isNode = pid != null && IsNodeProcess(pid.Value);
            var isHttp = IsHttpServer(host, port, 1500);

            if (isNode && isHttp)
            {
                return new HarnessDetection(HarnessPortStatus.HarnessRunning, pid);
            }
            return new HarnessDetection(HarnessPortStatus.ForeignOccupied, pid);
        }

        /// <summary>Returns the PID of the process listening on host:port, or null.</summary>
        public static int? FindOwnerPid(string host, int port) => TcpTable.FindOwnerPid(host, port);

        public static bool IsHttpServer(string host, int port, int timeoutMs = 1500)
        {
            try
            {
                using var client = new TcpClient();
                if (!client.ConnectAsync(host, port).Wait(timeoutMs))
                {
                    return false;
                }

                var stream = client.GetStream();
                stream.ReadTimeout = timeoutMs;
                var request = "GET / HTTP/1.1\r\nHost: " + host + ":" + port + "\r\nConnection: close\r\n\r\n";
                var bytes = Encoding.ASCII.GetBytes(request);
                stream.Write(bytes, 0, bytes.Length);

                var buffer = new byte[1024];
                var n = stream.Read(buffer, 0, buffer.Length);
                if (n <= 0)
                {
                    return false;
                }

                var head = Encoding.ASCII.GetString(buffer, 0, n);
                return head.StartsWith("HTTP/", StringComparison.OrdinalIgnoreCase);
            }
            catch
            {
                return false;
            }
        }

        private static bool IsNodeProcess(int pid)
        {
            try
            {
                using var p = Process.GetProcessById(pid);
                return p.ProcessName.Equals("node", StringComparison.OrdinalIgnoreCase);
            }
            catch
            {
                return false;
            }
        }
    }

    /// <summary>
    /// IPv4 port-owner lookup via iphlpapi GetExtendedTcpTable.
    /// </summary>
    internal static class TcpTable
    {
        private const int TcpTableOwnerPidAll = 5;
        private const uint AfInet = 2;
        private const uint MibTcpStateListen = 2;

        [DllImport("iphlpapi.dll", SetLastError = true)]
        private static extern uint GetExtendedTcpTable(
            IntPtr pTcpTable, ref int pdwSize, bool bOrder, uint ulAf, int tableClass, uint reserved);

        [StructLayout(LayoutKind.Sequential)]
        private struct MibTcpRowOwnerPid
        {
            public uint state;
            public uint localAddr;
            public uint localPort;
            public uint remoteAddr;
            public uint remotePort;
            public uint owningPid;
        }

        public static int? FindOwnerPid(string host, int port)
        {
            if (!TryResolveIpv4(host, out var target))
            {
                return null;
            }

            var size = 0;
            GetExtendedTcpTable(IntPtr.Zero, ref size, false, AfInet, TcpTableOwnerPidAll, 0);
            if (size <= 0)
            {
                return null;
            }

            var buffer = Marshal.AllocHGlobal(size);
            try
            {
                var result = GetExtendedTcpTable(buffer, ref size, false, AfInet, TcpTableOwnerPidAll, 0);
                if (result != 0)
                {
                    return null;
                }

                var rowCount = Marshal.ReadInt32(buffer);
                var rowPtr = buffer + 4;

                for (var i = 0; i < rowCount; i++)
                {
                    var row = Marshal.PtrToStructure<MibTcpRowOwnerPid>(rowPtr);
                    rowPtr += Marshal.SizeOf<MibTcpRowOwnerPid>();

                    if (row.state != MibTcpStateListen)
                    {
                        continue;
                    }

                    var localIp = new IPAddress(BitConverter.GetBytes(row.localAddr));
                    var localPort = (int)((row.localPort & 0xFF) << 8 | ((row.localPort >> 8) & 0xFF));

                    if (localIp.Equals(target) && localPort == port)
                    {
                        return (int)row.owningPid;
                    }
                }

                return null;
            }
            finally
            {
                Marshal.FreeHGlobal(buffer);
            }
        }

        private static bool TryResolveIpv4(string host, out IPAddress address)
        {
            if (IPAddress.TryParse(host, out var parsed))
            {
                address = parsed;
            }
            else
            {
                try
                {
                    address = Dns.GetHostAddresses(host)
                        .FirstOrDefault(a => a.AddressFamily == AddressFamily.InterNetwork)
                        ?? IPAddress.None;
                }
                catch
                {
                    address = IPAddress.None;
                }
            }
            return address.AddressFamily == AddressFamily.InterNetwork;
        }
    }
}
