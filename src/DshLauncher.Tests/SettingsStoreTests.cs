using System;
using System.IO;
using Xunit;
using DshLauncher.Services;

namespace DshLauncher.Tests
{
    public class SettingsStoreTests : IDisposable
    {
        private readonly string _dir;
        private readonly string _path;

        public SettingsStoreTests()
        {
            _dir = Path.Combine(Path.GetTempPath(), "dshlauncher-tests", Guid.NewGuid().ToString("N"));
            _path = Path.Combine(_dir, "config.json");
        }

        public void Dispose()
        {
            if (Directory.Exists(_dir))
            {
                Directory.Delete(_dir, true);
            }
        }

        [Fact]
        public void Load_WhenFileMissing_ReturnsDefaults()
        {
            var store = new SettingsStore(_path);
            var settings = store.Load();

            Assert.Equal(3080, settings.Port);
            Assert.Equal("127.0.0.1", settings.Host);
            Assert.True(settings.AutoOpenBrowser);
            Assert.True(settings.MinimizeToTray);
            Assert.False(settings.AutostartEnabled);
            Assert.True(settings.AutostartSilent);
            Assert.Equal(0, settings.AutostartDelaySeconds);
            Assert.False(settings.WatchdogEnabled);
        }

        [Fact]
        public void SaveThenLoad_RoundTripsAllValues()
        {
            var store = new SettingsStore(_path);
            var original = new AppSettings
            {
                Port = 8080,
                Host = "127.0.0.1",
                AutoOpenBrowser = false,
                AutostartEnabled = true,
                AutostartSilent = false,
                AutostartDelaySeconds = 5,
                WatchdogEnabled = true,
                MinimizeToTray = false,
                NodePath = @"C:\Program Files\nodejs\node.exe",
                DshBinPath = @"C:\npm\node_modules\@deepseek-ai\dsh\lib\bin.js",
                DshVersion = "1.2.3"
            };

            store.Save(original);
            var loaded = store.Load();

            Assert.Equal(8080, loaded.Port);
            Assert.Equal("127.0.0.1", loaded.Host);
            Assert.False(loaded.AutoOpenBrowser);
            Assert.True(loaded.AutostartEnabled);
            Assert.False(loaded.AutostartSilent);
            Assert.Equal(5, loaded.AutostartDelaySeconds);
            Assert.True(loaded.WatchdogEnabled);
            Assert.False(loaded.MinimizeToTray);
            Assert.Equal(@"C:\Program Files\nodejs\node.exe", loaded.NodePath);
            Assert.Equal(@"C:\npm\node_modules\@deepseek-ai\dsh\lib\bin.js", loaded.DshBinPath);
            Assert.Equal("1.2.3", loaded.DshVersion);
        }
    }
}
