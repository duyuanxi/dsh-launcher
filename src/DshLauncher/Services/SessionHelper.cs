using System;
using System.IO;

namespace DshLauncher.Services
{
    /// <summary>
    /// Materializes the embedded node helper (session-reader.mjs) next to the
    /// executable when it is missing, so a single-file build stays
    /// self-sufficient.
    /// </summary>
    public static class SessionHelper
    {
        public const string HelperFileName = "session-reader.mjs";

        public static string HelperPath => Path.Combine(AppContext.BaseDirectory, HelperFileName);

        public static void EnsureExtracted(string? targetDir = null)
        {
            var dir = targetDir ?? AppContext.BaseDirectory;
            var path = Path.Combine(dir, HelperFileName);
            if (File.Exists(path))
            {
                return;
            }

            var assembly = typeof(SessionHelper).Assembly;
            using var stream = assembly.GetManifestResourceStream("DshLauncher." + HelperFileName);
            if (stream == null)
            {
                return;
            }

            Directory.CreateDirectory(dir);
            using var fs = new FileStream(path, FileMode.Create, FileAccess.Write);
            stream.CopyTo(fs);
        }
    }
}
