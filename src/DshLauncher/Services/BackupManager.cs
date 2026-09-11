using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;

namespace DshLauncher.Services
{
    /// <summary>
    /// Zips the user's .dsh directory into a timestamped backup and restores it.
    /// Heavy, non-config directories (node_modules, cache) are excluded.
    /// </summary>
    public class BackupManager
    {
        private static readonly string[] DefaultExcludedDirs = { "node_modules", "cache" };
        private readonly string _backupDir;

        public BackupManager(string backupDir)
        {
            _backupDir = backupDir;
        }

        public string CreateBackup(string sourceDir)
        {
            Directory.CreateDirectory(_backupDir);
            var target = Path.Combine(_backupDir, "dsh-backup-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".zip");
            CreateBackupTo(sourceDir, target, DefaultExcludedDirs);
            return target;
        }

        public static void CreateBackupTo(string sourceDir, string targetZip, IReadOnlyCollection<string> excludedDirNames)
        {
            using var stream = new FileStream(targetZip, FileMode.CreateNew, FileAccess.Write);
            using var archive = new ZipArchive(stream, ZipArchiveMode.Create);
            var root = new DirectoryInfo(sourceDir);
            foreach (var file in EnumerateFiles(root, excludedDirNames))
            {
                var rel = Path.GetRelativePath(root.FullName, file.FullName);
                var entry = archive.CreateEntry(rel, CompressionLevel.Optimal);
                using var es = entry.Open();
                using var fs = file.OpenRead();
                fs.CopyTo(es);
            }
        }

        public static void RestoreTo(string backupZip, string targetDir)
        {
            var targetRoot = Path.GetFullPath(targetDir)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;

            using var stream = new FileStream(backupZip, FileMode.Open, FileAccess.Read);
            using var archive = new ZipArchive(stream, ZipArchiveMode.Read);
            foreach (var entry in archive.Entries)
            {
                var dest = Path.GetFullPath(Path.Combine(targetDir, entry.FullName));
                if (!dest.StartsWith(targetRoot, StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidOperationException("Backup contains an entry that escapes the target directory: " + entry.FullName);
                }

                if (entry.FullName.EndsWith("/") || entry.FullName.EndsWith("\\"))
                {
                    Directory.CreateDirectory(dest);
                    continue;
                }

                var parent = Path.GetDirectoryName(dest);
                if (!string.IsNullOrEmpty(parent))
                {
                    Directory.CreateDirectory(parent);
                }

                using var es = entry.Open();
                using var fs = new FileStream(dest, FileMode.Create, FileAccess.Write);
                es.CopyTo(fs);
            }
        }

        private static IEnumerable<FileInfo> EnumerateFiles(DirectoryInfo dir, IReadOnlyCollection<string> excluded)
        {
            foreach (var file in dir.EnumerateFiles())
            {
                yield return file;
            }

            foreach (var sub in dir.EnumerateDirectories())
            {
                if (excluded.Contains(sub.Name))
                {
                    continue;
                }

                foreach (var f in EnumerateFiles(sub, excluded))
                {
                    yield return f;
                }
            }
        }
    }
}
