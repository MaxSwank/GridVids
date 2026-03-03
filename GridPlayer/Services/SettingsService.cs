using GridVids.Models;
using System;
using System.IO;
using System.Text.Json;

namespace GridVids.Services
{
    public class SettingsService
    {
        private readonly string _settingsPath;

        public SettingsService()
        {
            try
            {
                // Save to AppData/Local/GridVids/settings.json
                var appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
                var folder = Path.Combine(appData, "GridVids");
                if (!Directory.Exists(folder))
                {
                    Directory.CreateDirectory(folder);
                }
                _settingsPath = Path.Combine(folder, "settings.json");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to determine settings path: {ex.Message}");
                _settingsPath = "settings.json"; // Fallback to local
            }
        }

        public void SaveSettings(AppSettings settings)
        {
            try
            {
                var json = JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(_settingsPath, json);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to save settings: {ex.Message}");
            }
        }

        public AppSettings LoadSettings()
        {
            try
            {
                if (File.Exists(_settingsPath))
                {
                    var json = File.ReadAllText(_settingsPath);
                    var settings = JsonSerializer.Deserialize<AppSettings>(json);
                    return settings ?? new AppSettings();
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to load settings: {ex.Message}");
            }
            return new AppSettings();
        }
    }
}
