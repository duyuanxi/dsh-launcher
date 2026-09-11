namespace DshLauncher.Services
{
    /// <summary>
    /// User-configurable settings persisted by <see cref="SettingsStore"/>.
    /// Property initializers are the defaults applied when a config file is
    /// missing or a field is absent from it.
    /// </summary>
    public class AppSettings
    {
        public int Port { get; set; } = 3080;
        public string Host { get; set; } = "127.0.0.1";
        public bool AutoOpenBrowser { get; set; } = true;
        public bool AutostartEnabled { get; set; }
        public bool AutostartSilent { get; set; } = true;
        public int AutostartDelaySeconds { get; set; }
        public bool WatchdogEnabled { get; set; }
        public bool MinimizeToTray { get; set; } = true;
        public string NodePath { get; set; } = "";
        public string DshBinPath { get; set; } = "";
        public string DshVersion { get; set; } = "";
    }
}
