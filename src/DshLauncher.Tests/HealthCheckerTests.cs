using System.Net;
using System.Net.Sockets;
using Xunit;
using DshLauncher.Services;

namespace DshLauncher.Tests
{
    public class HealthCheckerTests
    {
        [Fact]
        public void IsPortOpen_ReturnsTrueWhenListening()
        {
            var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            try
            {
                var port = ((IPEndPoint)listener.LocalEndpoint).Port;
                Assert.True(HealthChecker.IsPortOpen("127.0.0.1", port, 2000));
            }
            finally
            {
                listener.Stop();
            }
        }

        [Fact]
        public void IsPortOpen_ReturnsFalseWhenClosed()
        {
            var l = new TcpListener(IPAddress.Loopback, 0);
            l.Start();
            var port = ((IPEndPoint)l.LocalEndpoint).Port;
            l.Stop();

            Assert.False(HealthChecker.IsPortOpen("127.0.0.1", port, 500));
        }

        [Fact]
        public void ParseWebUrl_ExtractsUrl()
        {
            Assert.Equal("http://127.0.0.1:3080", HealthChecker.ParseWebUrl("dsh web: http://127.0.0.1:3080"));
            Assert.Equal(
                "http://127.0.0.1:3080",
                HealthChecker.ParseWebUrl("dsh web: http://127.0.0.1:3080 (LAN: http://192.168.1.5:3080)"));
            Assert.Null(HealthChecker.ParseWebUrl("no url here"));
        }
    }
}
