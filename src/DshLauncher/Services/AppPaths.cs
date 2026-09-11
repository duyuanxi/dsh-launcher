using System;
using System.IO;

namespace DshLauncher.Services
{
    /// <summary>
    /// Stable per-user paths for the launcher's data, logs, backups, and DSH home.
    /// </summary>
    public static class AppPaths
    {
        public static string LocalAppData => Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        public static string UserProfile => Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

        public static string AppDataDir => Path.Combine(LocalAppData, "DSHLauncher");
        public static string LogsDir => Path.Combine(AppDataDir, "logs");
        public static string BackupsDir => Path.Combine(AppDataDir, "backups");
        public static string DshLogPath => Path.Combine(LogsDir, "dsh.log");
        public static string LauncherLogPath => Path.Combine(LogsDir, "launcher.log");

        public static string DshHome => Path.Combine(UserProfile, ".dsh");
        public static string SessionsRoot => Path.Combine(DshHome, "sessions");

        public static string ProfileName => "web";
        public static string ProfileDir => Path.Combine(DshHome, "profiles", "web");
    }
}
