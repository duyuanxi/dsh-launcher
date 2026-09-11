using System;
using System.IO;
using System.Linq;
using Xunit;
using DshLauncher.Services;

namespace DshLauncher.Tests
{
    public class PluginManagerTests : IDisposable
    {
        private readonly string _root;
        private readonly string _profileDir;
        private readonly string _cliDir;

        public PluginManagerTests()
        {
            _root = Path.Combine(Path.GetTempPath(), "dshlauncher-tests", Guid.NewGuid().ToString("N"));
            _profileDir = Path.Combine(_root, "profiles", "web");
            _cliDir = Path.Combine(_root, "cli");
            Directory.CreateDirectory(_profileDir);
            Directory.CreateDirectory(_cliDir);

            File.WriteAllText(Path.Combine(_profileDir, "package.json"), @"{
  ""name"": ""dsh-profile-web"",
  ""private"": true,
  ""dependencies"": { ""dshmarket"": ""^1.0.0"", ""plain-lib"": ""^2.0.0"" },
  ""dsh"": { ""profile"": { ""bundles"": [""@deepseek-ai/dsh-base"", ""dshmarket"", ""ghost-plugin""] } }
}");

            File.WriteAllText(Path.Combine(_cliDir, "package.json"), @"{
  ""dependencies"": { ""@deepseek-ai/dsh-base"": ""0.1.1"", ""@deepseek-ai/dsh-web-app"": ""0.1.1"" }
}");

            // version for a managed plugin
            Directory.CreateDirectory(Path.Combine(_profileDir, "node_modules", "dshmarket"));
            File.WriteAllText(Path.Combine(_profileDir, "node_modules", "dshmarket", "package.json"), @"{ ""version"": ""1.2.3"" }");
        }

        public void Dispose()
        {
            if (Directory.Exists(_root))
            {
                Directory.Delete(_root, true);
            }
        }

        [Fact]
        public void ListPlugins_ClassifiesInBoxManagedStaleAndInactive()
        {
            var manager = new PluginManager(_profileDir, _cliDir);
            var plugins = manager.ListPlugins();

            Assert.Equal(4, plugins.Count);
            Assert.Equal(("@deepseek-ai/dsh-base", PluginStatus.InBox), (plugins[0].Name, plugins[0].Status));
            Assert.Equal(("dshmarket", PluginStatus.Managed), (plugins[1].Name, plugins[1].Status));
            Assert.Equal(("ghost-plugin", PluginStatus.Stale), (plugins[2].Name, plugins[2].Status));
            Assert.Equal(("plain-lib", PluginStatus.Inactive), (plugins[3].Name, plugins[3].Status));
        }

        [Fact]
        public void ListPlugins_ReadsVersionFromNodeModules()
        {
            var manager = new PluginManager(_profileDir, _cliDir);
            var dshmarket = manager.ListPlugins().Single(p => p.Name == "dshmarket");
            Assert.Equal("1.2.3", dshmarket.Version);
        }

        [Fact]
        public void ScanLeftovers_ReturnsOnlyStaleBundles()
        {
            var manager = new PluginManager(_profileDir, _cliDir);
            Assert.Equal(new[] { "ghost-plugin" }, manager.ScanLeftovers());
        }

        [Fact]
        public void RemoveStaleBundles_RemovesOnlyGivenNamesAndPreservesOthers()
        {
            var manager = new PluginManager(_profileDir, _cliDir);
            manager.RemoveStaleBundles(new[] { "ghost-plugin" });

            var json = File.ReadAllText(Path.Combine(_profileDir, "package.json"));
            Assert.DoesNotContain("ghost-plugin", json);
            Assert.Contains("@deepseek-ai/dsh-base", json);
            Assert.Contains("dshmarket", json);
            Assert.Contains("plain-lib", json); // dependency preserved
            Assert.Contains("\"private\": true", json); // extra field preserved

            var plugins = manager.ListPlugins();
            Assert.DoesNotContain(plugins, p => p.Name == "ghost-plugin");
            Assert.Equal(3, plugins.Count);
        }

        [Fact]
        public void ListPlugins_MissingProfileReturnsEmpty()
        {
            var manager = new PluginManager(Path.Combine(_root, "nope"), _cliDir);
            Assert.Empty(manager.ListPlugins());
        }
    }
}
