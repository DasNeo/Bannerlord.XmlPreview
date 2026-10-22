using Bannerlord.XmlPreview.Configuration;
using Bannerlord.XmlPreview.Interfaces;
using System.IO;
using System.Text.Json;

namespace Bannerlord.XmlPreview.Services;

public sealed class AppSettingsService : IAppSettingsService
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    private readonly object _sync = new();
    private readonly string _settingsFilePath;
    private AppSettings _settings;

    public AppSettingsService()
    {
        var settingsDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Bannerlord.XmlPreview");
        _settingsFilePath = Path.Combine(settingsDirectory, "settings.json");
        _settings = LoadSettings();
    }

    public string BannerlordInstallRoot
    {
        get
        {
            lock (_sync)
            {
                return _settings.BannerlordInstallRoot;
            }
        }
    }

    public bool TrySetBannerlordInstallRoot(string path, out string normalizedPath, out string errorMessage)
    {
        if (!TryValidateBannerlordInstallRoot(path, out normalizedPath, out errorMessage))
        {
            return false;
        }

        lock (_sync)
        {
            if (string.Equals(_settings.BannerlordInstallRoot, normalizedPath, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            _settings = new AppSettings { BannerlordInstallRoot = normalizedPath };
            SaveSettings(_settings);
        }

        return true;
    }

    private AppSettings LoadSettings()
    {
        try
        {
            if (File.Exists(_settingsFilePath))
            {
                var json = File.ReadAllText(_settingsFilePath);
                var settings = JsonSerializer.Deserialize<AppSettings>(json, JsonOptions);
                if (settings is not null &&
                    TryValidateBannerlordInstallRoot(settings.BannerlordInstallRoot, out var normalizedPath, out _))
                {
                    return settings with { BannerlordInstallRoot = normalizedPath };
                }
            }
        }
        catch
        {
        }

        return new AppSettings();
    }

    private void SaveSettings(AppSettings settings)
    {
        var directory = Path.GetDirectoryName(_settingsFilePath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var json = JsonSerializer.Serialize(settings, JsonOptions);
        File.WriteAllText(_settingsFilePath, json);
    }

    private static bool TryValidateBannerlordInstallRoot(string path, out string normalizedPath, out string errorMessage)
    {
        normalizedPath = string.Empty;
        errorMessage = string.Empty;

        if (string.IsNullOrWhiteSpace(path))
        {
            errorMessage = "Bannerlord installation path is empty.";
            return false;
        }

        try
        {
            normalizedPath = Path.GetFullPath(path.Trim());
        }
        catch (Exception exception)
        {
            errorMessage = $"Bannerlord installation path is invalid: {exception.Message}";
            return false;
        }

        if (!Directory.Exists(normalizedPath))
        {
            errorMessage = "Bannerlord installation folder does not exist.";
            return false;
        }

        var modulesPath = Path.Combine(normalizedPath, "Modules");
        if (!Directory.Exists(modulesPath))
        {
            errorMessage = "Selected folder does not contain a Bannerlord 'Modules' directory.";
            return false;
        }

        return true;
    }
}
