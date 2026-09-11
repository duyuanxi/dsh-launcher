using System;
using Microsoft.Win32;
using Xunit;
using DshLauncher.Services;

namespace DshLauncher.Tests
{
    public class AutostartManagerTests : IDisposable
    {
        private readonly string _subKey;

        public AutostartManagerTests()
        {
            _subKey = @"Software\DshLauncher.Tests\" + Guid.NewGuid().ToString("N");
        }

        public void Dispose()
        {
            Registry.CurrentUser.DeleteSubKeyTree(_subKey, throwOnMissingSubKey: false);
        }

        [Fact]
        public void BuildCommand_QuotesPathAndAppendsAutostart()
        {
            Assert.Equal(
                "\"C:\\a b\\DshLauncher.exe\" --autostart",
                AutostartManager.BuildCommand(@"C:\a b\DshLauncher.exe"));
        }

        [Fact]
        public void EnableThenDisable_TogglesValue()
        {
            var mgr = new AutostartManager(_subKey, "test");
            Assert.False(mgr.IsEnabled());

            mgr.Enable(@"C:\x\DshLauncher.exe");
            Assert.True(mgr.IsEnabled());
            Assert.Equal("\"C:\\x\\DshLauncher.exe\" --autostart", mgr.GetCommand());

            mgr.Disable();
            Assert.False(mgr.IsEnabled());
        }
    }
}
