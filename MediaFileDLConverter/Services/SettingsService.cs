using System.IO;
using System.Text.Json;

namespace MediaFileDLConverter.Services
{
    /// <summary>
    /// Persists user settings (like download directory) to a JSON file in AppData.
    /// </summary>
    public class SettingsService
    {
        private static readonly string SettingsDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "MediaFileDLConverter");

        private static readonly string SettingsFile = Path.Combine(SettingsDir, "settings.json");

        public AppSettings CurrentSettings { get; private set; } = new();

        public SettingsService()
        {
            Load();
        }

        /// <summary>
        /// Loads settings from disk. Falls back to defaults if file doesn't exist.
        /// </summary>
        public void Load()
        {
            try
            {
                if (File.Exists(SettingsFile))
                {
                    var json = File.ReadAllText(SettingsFile);
                    var loaded = JsonSerializer.Deserialize<AppSettings>(json);
                    if (loaded != null)
                    {
                        CurrentSettings = loaded;
                        return;
                    }
                }
            }
            catch
            {
                // Fall through to defaults
            }

            // Default download directory
            CurrentSettings = new AppSettings
            {
                DownloadDirectory = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                    "Downloads", "MediaFileDLConverter")
            };
        }

        /// <summary>
        /// Saves current settings to disk.
        /// </summary>
        public void Save()
        {
            try
            {
                Directory.CreateDirectory(SettingsDir);
                var json = JsonSerializer.Serialize(CurrentSettings, new JsonSerializerOptions
                {
                    WriteIndented = true
                });
                File.WriteAllText(SettingsFile, json);
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"Failed to save settings: {ex.Message}", ex);
            }
        }

        /// <summary>
        /// Updates the download directory and saves.
        /// </summary>
        public void SetDownloadDirectory(string path)
        {
            CurrentSettings.DownloadDirectory = path;
        }
    }

    /// <summary>
    /// Application settings model.
    /// </summary>
    public class AppSettings
    {
        public string DownloadDirectory { get; set; } = string.Empty;
    }
}
