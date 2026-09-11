using System;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using Xunit;
using DshLauncher.Services;

namespace DshLauncher.Tests
{
    public class HarnessDetectorTests
    {
        [Fact]
        public void IsHttpServer_ReturnsTrueForHttpResponse()
        {
            var listener = StartResponder("HTTP/1.1 200 OK\r\nContent-Length: 0\r\n\r\n", out var port);
            try
            {
                Assert.True(HarnessDetector.IsHttpServer("127.0.0.1", port, 2000));
            }
            finally
            {
                listener.Stop();
            }
        }

        [Fact]
        public void IsHttpServer_ReturnsFalseForNonHttpResponse()
        {
            var listener = StartResponder("hello-not-http", out var port);
            try
            {
                Assert.False(HarnessDetector.IsHttpServer("127.0.0.1", port, 2000));
            }
            finally
            {
                listener.Stop();
            }
        }

        [Fact]
        public void IsHttpServer_ReturnsFalseWhenClosed()
        {
            var l = new TcpListener(IPAddress.Loopback, 0);
            l.Start();
            var port = ((IPEndPoint)l.LocalEndpoint).Port;
            l.Stop();

            Assert.False(HarnessDetector.IsHttpServer("127.0.0.1", port, 500));
        }

        [Fact]
        public void FindOwnerPid_ReturnsCurrentProcessForOwnedPort()
        {
            var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            try
            {
                var port = ((IPEndPoint)listener.LocalEndpoint).Port;
                var pid = HarnessDetector.FindOwnerPid("127.0.0.1", port);
                Assert.Equal(Process.GetCurrentProcess().Id, pid);
            }
            finally
            {
                listener.Stop();
            }
        }

        private static TcpListener StartResponder(string responseText, out int port)
        {
            var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            port = ((IPEndPoint)listener.LocalEndpoint).Port;

            var thread = new Thread(() =>
            {
                try
                {
                    while (true)
                    {
                        using var client = listener.AcceptTcpClient();
                        var stream = client.GetStream();
                        var buf = new byte[1024];
                        stream.Read(buf, 0, buf.Length); // drain the request
                        var resp = Encoding.ASCII.GetBytes(responseText);
                        stream.Write(resp, 0, resp.Length);
                        stream.Flush();
                    }
                }
                catch
                {
                    // listener stopped
                }
            })
            {
                IsBackground = true
            };
            thread.Start();
            return listener;
        }
    }
}
