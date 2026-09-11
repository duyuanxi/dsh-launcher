using System;
using System.IO;
using System.Text.Json;

namespace DshLauncher.Services
{
    /// <summary>
    /// Loads and saves the launcher configuration as JSON.
    /// </summary>
    public class SettingsStore
    {
        private readonly string _filePath;

        public SettingsStore(string filePath)
        {
            _filePath = filePath;
        }

        public static string DefaultFilePath
        {
            get
            {
                var baseDir = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
                return Path.Combine(baseDir, "DSHLauncher", "config.json");
            }
        }

        public AppSettings Load()
        {
            try
            {
                if (!File.Exists(_filePath))
                {
                    return new AppSettings();
                }

                var json = File.ReadAllText(_filePath);
                return JsonSerializer.Deserialize<AppSettings>(json) ?? new AppSettings();
            }
            catch
            {
                // Missing, locked, or corrupt config: fall back to defaults.
                return new AppSettings();
            }
        }

        public void Save(AppSettings settings)
        {
            var dir = Path.GetDirectoryName(_filePath);
            if (!string.IsNullOrEmpty(dir))
            {
                Directory.CreateDirectory(dir);
            }

            var json = JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(_filePath, json);
        }
    }
}
