using System;
using System.IO;
using System.Text.Json;

namespace Ballknower.Config;

public class SettingsStore
{
    private readonly string _settingsPath;

    public SettingsStore()
    {
        var appFolder = Path.Combine(
            Environment.GetFolderPath(
                Environment.SpecialFolder.LocalApplicationData),
            "Ballknower");

        Directory.CreateDirectory(appFolder);

        _settingsPath = Path.Combine(
            appFolder,
            "settings.json");
    }

    public AppSettings Load()
    {
        if (!File.Exists(_settingsPath))
        {
            return new AppSettings();
        }

        var json = File.ReadAllText(_settingsPath);

        return JsonSerializer.Deserialize<AppSettings>(json)
            ?? new AppSettings();
    }

    public void Save(AppSettings settings)
    {
        var json = JsonSerializer.Serialize(
            settings,
            new JsonSerializerOptions
            {
                WriteIndented = true
            });

        File.WriteAllText(
            _settingsPath,
            json);
    }
}