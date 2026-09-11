using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace DshLauncher.Services
{
    public enum PluginStatus
    {
        InBox,   // official bundle: in bundles, not a dependency, resolved from the CLI
        Managed, // in profile dependencies (active plugin, installable/uninstallable)
        Stale,   // in bundles but neither a dependency nor official — leftover from an uninstall
        Inactive // in dependencies but not loaded as a bundle
    }

    public class PluginInfo
    {
        public string Name { get; }
        public string Version { get; }
        public PluginStatus Status { get; }

        public PluginInfo(string name, string version, PluginStatus status)
        {
            Name = name;
            Version = version;
            Status = status;
        }
    }

    /// <summary>
    /// Reads and reconciles the DSH profile's plugin manifest (package.json).
    /// Pure file logic so it is unit-testable against temp directories.
    /// </summary>
    public class PluginManager
    {
        private readonly string _profileDir;
        private readonly string _cliPackageDir;

        public PluginManager(string profileDir, string cliPackageDir)
        {
            _profileDir = profileDir;
            _cliPackageDir = cliPackageDir;
        }

        public IReadOnlyList<PluginInfo> ListPlugins()
        {
            var (deps, bundles) = ReadManifest(_profileDir);
            var depSet = new HashSet<string>(deps, StringComparer.Ordinal);
            var cliDeps = ReadCliDependencies(_cliPackageDir);

            var result = new List<PluginInfo>();
            var seen = new HashSet<string>(StringComparer.Ordinal);

            foreach (var name in bundles)
            {
                seen.Add(name);
                result.Add(new PluginInfo(name, ReadVersion(name), Classify(name, depSet, cliDeps)));
            }

            foreach (var name in deps)
            {
                if (!seen.Add(name))
                {
                    continue;
                }
                result.Add(new PluginInfo(name, ReadVersion(name), PluginStatus.Inactive));
            }

            return result;
        }

        /// <summary>Returns bundle entries that are neither a dependency nor an official bundle.</summary>
        public IReadOnlyList<string> ScanLeftovers()
        {
            var (deps, bundles) = ReadManifest(_profileDir);
            var depSet = new HashSet<string>(deps, StringComparer.Ordinal);
            var cliDeps = ReadCliDependencies(_cliPackageDir);
            return bundles.Where(n => !depSet.Contains(n) && !cliDeps.Contains(n)).ToList();
        }

        /// <summary>Removes stale bundle names from dsh.profile.bundles, preserving the rest of the manifest.</summary>
        public void RemoveStaleBundles(IReadOnlyCollection<string> names)
        {
            var path = Path.Combine(_profileDir, "package.json");
            if (!File.Exists(path))
            {
                return;
            }

            var manifest = JsonSerializer.Deserialize<ProfileManifest>(File.ReadAllText(path));
            var bundles = manifest?.Dsh?.Profile?.Bundles;
            if (bundles == null)
            {
                return;
            }

            var remove = new HashSet<string>(names, StringComparer.Ordinal);
            var before = bundles.Count;
            var kept = bundles.Where(n => !remove.Contains(n)).ToList();
            if (kept.Count == before)
            {
                return;
            }

            manifest!.Dsh!.Profile!.Bundles = kept;
            File.WriteAllText(path, JsonSerializer.Serialize(manifest, new JsonSerializerOptions { WriteIndented = true }));
        }

        private static PluginStatus Classify(string name, HashSet<string> deps, HashSet<string> cliDeps)
        {
            if (deps.Contains(name))
            {
                return PluginStatus.Managed;
            }
            if (cliDeps.Contains(name))
            {
                return PluginStatus.InBox;
            }
            return PluginStatus.Stale;
        }

        private string ReadVersion(string name)
        {
            foreach (var root in new[] { _profileDir, _cliPackageDir })
            {
                var p = Path.Combine(root, "node_modules", name, "package.json");
                if (File.Exists(p))
                {
                    var v = ReadVersionField(p);
                    if (v != null)
                    {
                        return v;
                    }
                }
            }
            return "未知";
        }

        private static string? ReadVersionField(string packageJsonPath)
        {
            try
            {
                using var doc = JsonDocument.Parse(File.ReadAllText(packageJsonPath));
                return doc.RootElement.TryGetProperty("version", out var v) ? v.GetString() : null;
            }
            catch
            {
                return null;
            }
        }

        private static (string[] deps, string[] bundles) ReadManifest(string dir)
        {
            var path = Path.Combine(dir, "package.json");
            if (!File.Exists(path))
            {
                return (Array.Empty<string>(), Array.Empty<string>());
            }

            try
            {
                using var doc = JsonDocument.Parse(File.ReadAllText(path));

                var deps = new List<string>();
                if (doc.RootElement.TryGetProperty("dependencies", out var d) && d.ValueKind == JsonValueKind.Object)
                {
                    foreach (var p in d.EnumerateObject())
                    {
                        deps.Add(p.Name);
                    }
                }

                var bundles = new List<string>();
                if (doc.RootElement.TryGetProperty("dsh", out var dsh) &&
                    dsh.TryGetProperty("profile", out var prof) &&
                    prof.TryGetProperty("bundles", out var b) && b.ValueKind == JsonValueKind.Array)
                {
                    foreach (var e in b.EnumerateArray())
                    {
                        if (e.ValueKind == JsonValueKind.String)
                        {
                            bundles.Add(e.GetString()!);
                        }
                    }
                }

                return (deps.ToArray(), bundles.ToArray());
            }
            catch
            {
                return (Array.Empty<string>(), Array.Empty<string>());
            }
        }

        private static HashSet<string> ReadCliDependencies(string cliPackageDir)
        {
            var set = new HashSet<string>(StringComparer.Ordinal);
            var path = Path.Combine(cliPackageDir, "package.json");
            if (!File.Exists(path))
            {
                return set;
            }

            try
            {
                using var doc = JsonDocument.Parse(File.ReadAllText(path));
                if (doc.RootElement.TryGetProperty("dependencies", out var d) && d.ValueKind == JsonValueKind.Object)
                {
                    foreach (var p in d.EnumerateObject())
                    {
                        set.Add(p.Name);
                    }
                }
            }
            catch { /* ignore malformed manifest */ }

            return set;
        }
    }

    internal class ProfileManifest
    {
        [JsonPropertyName("dsh")]
        public DshSection? Dsh { get; set; }

        [JsonExtensionData]
        public Dictionary<string, JsonElement>? Extra { get; set; }
    }

    internal class DshSection
    {
        [JsonPropertyName("profile")]
        public ProfileSection? Profile { get; set; }

        [JsonExtensionData]
        public Dictionary<string, JsonElement>? Extra { get; set; }
    }

    internal class ProfileSection
    {
        [JsonPropertyName("bundles")]
        public List<string>? Bundles { get; set; }

        [JsonExtensionData]
        public Dictionary<string, JsonElement>? Extra { get; set; }
    }
}
