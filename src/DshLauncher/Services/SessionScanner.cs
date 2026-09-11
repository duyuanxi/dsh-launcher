using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace DshLauncher.Services
{
    public readonly struct SessionInfo
    {
        public string FilePath { get; }
        public DateTime LastWriteTimeUtc { get; }

        public SessionInfo(string filePath, DateTime lastWriteTimeUtc)
        {
            FilePath = filePath;
            LastWriteTimeUtc = lastWriteTimeUtc;
        }
    }

    /// <summary>
    /// Lists recent DSH session files under a sessions root, newest first.
    /// </summary>
    public class SessionScanner
    {
        private readonly string _root;

        public SessionScanner(string root)
        {
            _root = root;
        }

        public IReadOnlyList<SessionInfo> Scan(int max = 50)
        {
            if (!Directory.Exists(_root))
            {
                return Array.Empty<SessionInfo>();
            }

            return Directory.EnumerateFiles(_root, "*", SearchOption.AllDirectories)
                .Where(IsSessionFile)
                .Select(f => new SessionInfo(f, File.GetLastWriteTimeUtc(f)))
                .OrderByDescending(s => s.LastWriteTimeUtc)
                .Take(max)
                .ToList();
        }

        /// <summary>DSH writes sessions as <c>session.jsonl</c> or <c>session.jsonl.zstd</c>.</summary>
        private static bool IsSessionFile(string path)
        {
            var name = Path.GetFileName(path);
            return name == "session.jsonl" || name == "session.jsonl.zstd";
        }
    }
}
