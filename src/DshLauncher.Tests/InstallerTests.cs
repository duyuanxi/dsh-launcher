using System;
using System.IO;
using System.Linq;
using Xunit;
using DshLauncher.Services;

namespace DshLauncher.Tests
{
    public class InstallerServiceTests
    {
        [Fact]
        public void Plan_AllPresent_SkipsNpmAndListsRemainingSteps()
        {
            var plan = InstallerService.Plan(nodeFound: true, dshInstalled: true, alreadyInstalled: false);

            Assert.True(plan.CanInstall);
            Assert.DoesNotContain(plan.Steps, s => s.Contains("npm"));
            Assert.Contains(plan.Steps, s => s.Contains("复制启动器"));
            Assert.Contains(plan.Steps, s => s.Contains("快捷方式"));
            Assert.Contains(plan.Steps, s => s.Contains("开机自启"));
        }

        [Fact]
        public void Plan_DshMissing_IncludesNpmStep()
        {
            var plan = InstallerService.Plan(nodeFound: true, dshInstalled: false, alreadyInstalled: false);

            Assert.True(plan.CanInstall);
            Assert.Contains(plan.Steps, s => s.Contains("@deepseek-ai/dsh"));
        }

        [Fact]
        public void Plan_NodeMissing_CannotInstall()
        {
            var plan = InstallerService.Plan(nodeFound: false, dshInstalled: false, alreadyInstalled: false);

            Assert.False(plan.CanInstall);
            Assert.Single(plan.Steps);
            Assert.Contains("Node.js", plan.Steps[0]);
        }
    }

    public class SessionHelperTests
    {
        [Fact]
        public void EnsureExtracted_WritesEmbeddedHelper()
        {
            var dir = Path.Combine(Path.GetTempPath(), "dshlauncher-tests", Guid.NewGuid().ToString("N"));
            try
            {
                SessionHelper.EnsureExtracted(dir);

                var path = Path.Combine(dir, "session-reader.mjs");
                Assert.True(File.Exists(path));
                var content = File.ReadAllText(path);
                Assert.Contains("DSH session log reader", content);
                Assert.Contains("zstdDecompressSync", content);
            }
            finally
            {
                if (Directory.Exists(dir))
                {
                    Directory.Delete(dir, true);
                }
            }
        }

        [Fact]
        public void EnsureExtracted_IdempotentWhenFileExists()
        {
            var dir = Path.Combine(Path.GetTempPath(), "dshlauncher-tests", Guid.NewGuid().ToString("N"));
            try
            {
                SessionHelper.EnsureExtracted(dir);
                var first = File.ReadAllText(Path.Combine(dir, "session-reader.mjs"));
                SessionHelper.EnsureExtracted(dir);
                var second = File.ReadAllText(Path.Combine(dir, "session-reader.mjs"));
                Assert.Equal(first, second);
            }
            finally
            {
                if (Directory.Exists(dir))
                {
                    Directory.Delete(dir, true);
                }
            }
        }
    }

    public class DshResolverNodeTests
    {
        [Fact]
        public void FindNodePath_ReturnsNodeOnThisMachine()
        {
            var node = DshResolver.FindNodePath();
            Assert.NotNull(node);
            Assert.True(File.Exists(node));
        }
    }
}
