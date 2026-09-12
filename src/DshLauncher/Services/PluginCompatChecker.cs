using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace DshLauncher.Services
{
    public class PluginCompatIssue
    {
        public string PluginName { get; }
        public string PeerName { get; }
        public string RequiredRange { get; }
        public string InstalledVersion { get; }

        public PluginCompatIssue(string pluginName, string peerName, string requiredRange, string installedVersion)
        {
            PluginName = pluginName;
            PeerName = peerName;
            RequiredRange = requiredRange;
            InstalledVersion = installedVersion;
        }

        public override string ToString() => $"{PluginName} 需要 {PeerName} {RequiredRange}（当前 {InstalledVersion}）";
    }

    /// <summary>
    /// Checks every installed profile plugin's peerDependencies against the
    /// versions actually installed (profile node_modules first, then the
    /// harness CLI's bundled node_modules).
    /// </summary>
    public static class PluginCompatChecker
    {
        public static IReadOnlyList<PluginCompatIssue> Check(string profileDir, string cliPackageDir)
        {
            var issues = new List<PluginCompatIssue>();
            var deps = ReadDependencyNames(Path.Combine(profileDir, "package.json"));

            foreach (var dep in deps)
            {
                var manifest = Path.Combine(profileDir, "node_modules", dep, "package.json");
                if (!File.Exists(manifest))
                {
                    continue; // not installed — the reinstall step handles that
                }

                foreach (var (peerName, range) in ReadPeerDependencies(manifest))
                {
                    var installed = FindInstalledVersion(profileDir, cliPackageDir, peerName);
                    if (installed == null)
                    {
                        if (peerName.StartsWith("@deepseek-ai/", StringComparison.Ordinal))
                        {
                            issues.Add(new PluginCompatIssue(dep, peerName, range, "未安装"));
                        }
                        continue;
                    }

                    if (!SemVerRange.Satisfies(installed, range))
                    {
                        issues.Add(new PluginCompatIssue(dep, peerName, range, installed));
                    }
                }
            }

            return issues;
        }

        internal static IReadOnlyList<string> ReadDependencyNames(string profileManifestPath)
        {
            if (!File.Exists(profileManifestPath))
            {
                return Array.Empty<string>();
            }
            try
            {
                using var doc = JsonDocument.Parse(File.ReadAllText(profileManifestPath));
                var names = new List<string>();
                if (doc.RootElement.TryGetProperty("dependencies", out var d) && d.ValueKind == JsonValueKind.Object)
                {
                    foreach (var p in d.EnumerateObject())
                    {
                        names.Add(p.Name);
                    }
                }
                return names;
            }
            catch
            {
                return Array.Empty<string>();
            }
        }

        internal static IEnumerable<(string name, string range)> ReadPeerDependencies(string pluginManifestPath)
        {
            if (!File.Exists(pluginManifestPath))
            {
                yield break;
            }

            JsonDocument doc;
            try
            {
                doc = JsonDocument.Parse(File.ReadAllText(pluginManifestPath));
            }
            catch
            {
                yield break;
            }

            using (doc)
            {
                if (!doc.RootElement.TryGetProperty("peerDependencies", out var peers) ||
                    peers.ValueKind != JsonValueKind.Object)
                {
                    yield break;
                }
                foreach (var p in peers.EnumerateObject())
                {
                    if (p.Value.ValueKind == JsonValueKind.String)
                    {
                        yield return (p.Name, p.Value.GetString() ?? "");
                    }
                }
            }
        }

        private static string? FindInstalledVersion(string profileDir, string cliPackageDir, string packageName)
        {
            foreach (var root in new[] { profileDir, cliPackageDir })
            {
                var manifest = Path.Combine(root, "node_modules", packageName, "package.json");
                if (!File.Exists(manifest))
                {
                    continue;
                }
                try
                {
                    using var doc = JsonDocument.Parse(File.ReadAllText(manifest));
                    if (doc.RootElement.TryGetProperty("version", out var v) && v.ValueKind == JsonValueKind.String)
                    {
                        return v.GetString();
                    }
                }
                catch { /* ignore malformed manifest */ }
            }
            return null;
        }
    }
}
