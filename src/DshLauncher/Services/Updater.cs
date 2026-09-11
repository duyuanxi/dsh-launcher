using System.IO;
using System.Text.Json;

namespace DshLauncher.Services
{
    /// <summary>
    /// dsh update support: read the installed version and compare versions.
    /// The npm process invocation lives in the UI layer; this keeps the pure
    /// parts testable.
    /// </summary>
    public static class Updater
    {
        public static string? ReadInstalledVersion(string dshPackageDir)
        {
            var manifestPath = Path.Combine(dshPackageDir, "package.json");
            if (!File.Exists(manifestPath))
            {
                return null;
            }

            try
            {
                using var doc = JsonDocument.Parse(File.ReadAllText(manifestPath));
                return doc.RootElement.TryGetProperty("version", out var v) ? v.GetString() : null;
            }
            catch
            {
                return null;
            }
        }
    }
}
