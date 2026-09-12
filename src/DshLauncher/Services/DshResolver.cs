using System;
using System.IO;

namespace DshLauncher.Services
{
    public class DshLocation
    {
        public string NodePath { get; }
        public string BinPath { get; }
        public string Version { get; }

        public DshLocation(string nodePath, string binPath, string version)
        {
            NodePath = nodePath;
            BinPath = binPath;
            Version = version;
        }
    }

    /// <summary>
    /// Locates a stable, globally-installed @deepseek-ai/dsh (and node.exe) so
    /// the launcher never depends on the transient npx cache.
    /// </summary>
    public class DshResolver
    {
        public DshLocation? Resolve()
        {
            var nodePath = FindNode();
            var binPath = FindDshBin();
            if (nodePath == null || binPath == null)
            {
                return null;
            }

            var pkgDir = Path.GetDirectoryName(Path.GetDirectoryName(binPath));
            var version = pkgDir != null ? Updater.ReadInstalledVersion(pkgDir) : null;
            return new DshLocation(nodePath, binPath, version ?? "");
        }

        private static string? FindNode()
        {
            var candidates = new[]
            {
                @"C:\Program Files\nodejs\node.exe",
                @"C:\Program Files (x86)\nodejs\node.exe"
            };
            foreach (var c in candidates)
            {
                if (File.Exists(c))
                {
                    return c;
                }
            }
            return FindOnPath("node.exe");
        }

        /// <summary>Locates node.exe on this machine, or null when Node.js is missing.</summary>
        public static string? FindNodePath() => FindNode();

        private static string? FindDshBin()
        {
            var root = GetNpmGlobalRoot();
            if (root != null)
            {
                var bin = Path.Combine(root, "@deepseek-ai", "dsh", "lib", "bin.js");
                if (File.Exists(bin))
                {
                    return bin;
                }
            }
            return null;
        }

        private static string? GetNpmGlobalRoot()
        {
            var roaming = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            var candidate = Path.Combine(roaming, "npm", "node_modules");
            return Directory.Exists(candidate) ? candidate : null;
        }

        private static string? FindOnPath(string exe)
        {
            var pathEnv = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
            foreach (var dir in pathEnv.Split(';'))
            {
                if (string.IsNullOrWhiteSpace(dir))
                {
                    continue;
                }

                try
                {
                    var full = Path.Combine(dir.Trim(), exe);
                    if (File.Exists(full))
                    {
                        return full;
                    }
                }
                catch { /* malformed PATH entry */ }
            }
            return null;
        }
    }
}
