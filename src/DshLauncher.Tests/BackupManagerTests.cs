using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using Xunit;
using DshLauncher.Services;

namespace DshLauncher.Tests
{
    public class BackupManagerTests : IDisposable
    {
        private readonly string _dir;

        public BackupManagerTests()
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
        public void CreateBackupTo_ExcludesNodeModulesAndCache()
        {
            var src = Path.Combine(_dir, "src");
            Directory.CreateDirectory(Path.Combine(src, "node_modules"));
            Directory.CreateDirectory(Path.Combine(src, "cache"));
            File.WriteAllText(Path.Combine(src, "keep.txt"), "keep");
            File.WriteAllText(Path.Combine(src, "node_modules", "x.txt"), "x");
            File.WriteAllText(Path.Combine(src, "cache", "y.txt"), "y");
            var zip = Path.Combine(_dir, "b.zip");

            BackupManager.CreateBackupTo(src, zip, new[] { "node_modules", "cache" });

            using var archive = ZipFile.OpenRead(zip);
            var names = archive.Entries.Select(e => e.FullName).ToList();
            Assert.Contains("keep.txt", names);
            Assert.DoesNotContain(names, n => n.Contains("node_modules"));
            Assert.DoesNotContain(names, n => n.Contains("cache"));
        }

        [Fact]
        public void RestoreTo_RoundTripsFiles()
        {
            var src = Path.Combine(_dir, "src");
            Directory.CreateDirectory(Path.Combine(src, "sub"));
            File.WriteAllText(Path.Combine(src, "a.txt"), "hello");
            File.WriteAllText(Path.Combine(src, "sub", "b.txt"), "world");
            var zip = Path.Combine(_dir, "b.zip");
            BackupManager.CreateBackupTo(src, zip, Array.Empty<string>());

            var dst = Path.Combine(_dir, "dst");
            BackupManager.RestoreTo(zip, dst);

            Assert.Equal("hello", File.ReadAllText(Path.Combine(dst, "a.txt")));
            Assert.Equal("world", File.ReadAllText(Path.Combine(dst, "sub", "b.txt")));
        }

        [Fact]
        public void RestoreTo_RejectsZipSlip()
        {
            var zip = Path.Combine(_dir, "evil.zip");
            using (var stream = new FileStream(zip, FileMode.CreateNew))
            using (var archive = new ZipArchive(stream, ZipArchiveMode.Create))
            {
                var entry = archive.CreateEntry("../evil.txt");
                using var es = entry.Open();
                using var sw = new StreamWriter(es);
                sw.Write("pwned");
            }

            var dst = Path.Combine(_dir, "dst");
            Assert.Throws<InvalidOperationException>(() => BackupManager.RestoreTo(zip, dst));
        }
    }
}
