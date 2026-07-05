using System;
using System.IO;
using System.Text.Json;

namespace Hale_Marketing_International.Security
{
    /// <summary>
    /// Persists per-device settings in %AppData%\HaleMarketingPOS\settings.json
    /// Master key is NEVER stored — it is derived at runtime from Google email.
    /// </summary>
    public class AppSettings
    {
        private static AppSettings? _instance;
        public static AppSettings Instance => _instance ??= Load();

        // Google account connected on this device
        public string ConnectedGoogleEmail { get; set; } = string.Empty;
        public bool GoogleDriveConfigured { get; set; } = false;
        public DateTime? LastBackupTime { get; set; }

        // ── Paths ─────────────────────────────────────────────────────────────

        private static string AppDataFolder =>
            Path.Combine(Environment.GetFolderPath(
                Environment.SpecialFolder.ApplicationData), "HaleMarketingPOS");

        public static string SettingsFilePath =>
            Path.Combine(AppDataFolder, "settings.json");

        public static string GoogleTokenFolder =>
            Path.Combine(AppDataFolder, "GoogleToken");

        // Same DB path as UserService ("Data Source=posdata.db")
        public static string DatabasePath =>
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "posdata.db");

        // ── Load / Save ───────────────────────────────────────────────────────

        private static AppSettings Load()
        {
            Directory.CreateDirectory(AppDataFolder);
            if (!File.Exists(SettingsFilePath)) return new AppSettings();
            try
            {
                string json = File.ReadAllText(SettingsFilePath);
                return JsonSerializer.Deserialize<AppSettings>(json) ?? new AppSettings();
            }
            catch { return new AppSettings(); }
        }

        public void Save()
        {
            Directory.CreateDirectory(AppDataFolder);
            File.WriteAllText(SettingsFilePath,
                JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
        }

        public bool IsSetupComplete => File.Exists(DatabasePath);
    }
}