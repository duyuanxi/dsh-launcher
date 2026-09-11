using System;
using System.IO;
using Xunit;
using DshLauncher.Services;

namespace DshLauncher.Tests
{
    public class UpdaterTests : IDisposable
    {
        private readonly string _dir;

        public UpdaterTests()
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
        public void ReadInstalledVersion_ReadsVersionFromPackageJson()
        {
            File.WriteAllText(Path.Combine(_dir, "package.json"), "{ \"version\": \"0.1.1-rc.2\" }");
            Assert.Equal("0.1.1-rc.2", Updater.ReadInstalledVersion(_dir));
        }

        [Fact]
        public void ReadInstalledVersion_ReturnsNullWhenMissing()
        {
            Assert.Null(Updater.ReadInstalledVersion(_dir));
        }
    }
}
