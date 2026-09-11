using System;
using System.IO;
using Xunit;
using DshLauncher.Services;

namespace DshLauncher.Tests
{
    public class SessionTitleReaderTests : IDisposable
    {
        private readonly string _dir;

        public SessionTitleReaderTests()
        {
            _dir = Path.Combine(Path.GetTempPath(), "dshlauncher-tests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_dir);
        }

        public void Dispose()
        {
            if (Directory.Exists(_dir))
            {
                Directory.Delete(_dir, true);
            }
        }

        private string? FindNode() => new DshResolver().Resolve()?.NodePath;

        private string WriteHelper(string script)
        {
            var path = Path.Combine(_dir, "fake-helper.mjs");
            File.WriteAllText(path, script);
            return path;
        }

        [Fact]
        public void Read_ParsesJsonFromHelper()
        {
            var node = FindNode();
            if (node == null)
            {
                return; // node not installed: nothing to verify against
            }

            var helper = WriteHelper(
                "console.log(JSON.stringify({id:'s1',createdAt:1700000000000,cwd:'C:\\\\ws',agentPreset:'code',title:'测试标题'}));");
            var reader = new SessionTitleReader(node, helper);

            var meta = reader.Read(Path.Combine(_dir, "whatever.zstd"));

            Assert.NotNull(meta);
            Assert.Equal("s1", meta!.Id);
            Assert.Equal(1700000000000L, meta.CreatedAt);
            Assert.Equal(@"C:\ws", meta.Cwd);
            Assert.Equal("code", meta.AgentPreset);
            Assert.Equal("测试标题", meta.Title);
        }

        [Fact]
        public void Read_ReturnsNullWhenHelperFails()
        {
            var node = FindNode();
            if (node == null)
            {
                return;
            }

            var helper = WriteHelper("console.error('boom'); process.exit(1);");
            var reader = new SessionTitleReader(node, helper);

            Assert.Null(reader.Read(Path.Combine(_dir, "whatever.zstd")));
        }

        [Fact]
        public void Read_ReturnsNullForMissingNode()
        {
            var helper = WriteHelper("console.log('{}');");
            var reader = new SessionTitleReader(Path.Combine(_dir, "no-such-node.exe"), helper);
            Assert.Null(reader.Read(Path.Combine(_dir, "whatever.zstd")));
        }
    }
}
