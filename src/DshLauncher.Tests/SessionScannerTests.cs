using System;
using System.IO;
using Xunit;
using DshLauncher.Services;

namespace DshLauncher.Tests
{
    public class SessionScannerTests : IDisposable
    {
        private readonly string _dir;

        public SessionScannerTests()
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

        [Fact]
        public void Scan_FindsZstdAndPlainSessionFilesNewestFirst()
        {
            var a = Path.Combine(_dir, "ws-a", "s1", "session.jsonl.zstd");
            var sub = Path.Combine(_dir, "ws-b", "s2");
            Directory.CreateDirectory(Path.GetDirectoryName(a)!);
            Directory.CreateDirectory(sub);
            var b = Path.Combine(sub, "session.jsonl");

            File.WriteAllText(a, "x");
            File.WriteAllText(b, "y");
            File.WriteAllText(Path.Combine(_dir, "ws-a", "s1", "session.jsonl.zstd.tmp"), "noise");
            File.WriteAllText(Path.Combine(_dir, "other.jsonl"), "not-a-session");

            File.SetLastWriteTimeUtc(a, new DateTime(2020, 1, 1));
            File.SetLastWriteTimeUtc(b, new DateTime(2021, 1, 1));

            var result = new SessionScanner(_dir).Scan();

            Assert.Equal(2, result.Count);
            Assert.Equal(b, result[0].FilePath);
            Assert.Equal(a, result[1].FilePath);
        }

        [Fact]
        public void Scan_MissingRootReturnsEmpty()
        {
            Assert.Empty(new SessionScanner(Path.Combine(_dir, "nope")).Scan());
        }
    }
}
