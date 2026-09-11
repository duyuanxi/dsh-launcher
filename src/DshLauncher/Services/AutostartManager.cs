using Microsoft.Win32;

namespace DshLauncher.Services
{
    /// <summary>
    /// Reads and writes the per-user "run at logon" registry value
    /// (HKCU\...\Run) that makes the launcher auto-start.
    /// </summary>
    public class AutostartManager
    {
        public const string DefaultRunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
        public const string DefaultValueName = "DshLauncher";

        private readonly string _runKeyPath;
        private readonly string _valueName;

        public AutostartManager(string? runKeyPath = null, string? valueName = null)
        {
            _runKeyPath = runKeyPath ?? DefaultRunKeyPath;
            _valueName = valueName ?? DefaultValueName;
        }

        /// <summary>Build the command line stored in the Run value.</summary>
        public static string BuildCommand(string exePath) => "\"" + exePath + "\" --autostart";

        public bool IsEnabled()
        {
            using var key = Registry.CurrentUser.OpenSubKey(_runKeyPath, writable: false);
            return key?.GetValue(_valueName) != null;
        }

        public string? GetCommand()
        {
            using var key = Registry.CurrentUser.OpenSubKey(_runKeyPath, writable: false);
            return key?.GetValue(_valueName) as string;
        }

        public void Enable(string exePath)
        {
            using var key = Registry.CurrentUser.CreateSubKey(_runKeyPath, writable: true);
            key.SetValue(_valueName, BuildCommand(exePath), RegistryValueKind.String);
        }

        public void Disable()
        {
            using var key = Registry.CurrentUser.OpenSubKey(_runKeyPath, writable: true);
            key?.DeleteValue(_valueName, throwOnMissingValue: false);
        }
    }
}
