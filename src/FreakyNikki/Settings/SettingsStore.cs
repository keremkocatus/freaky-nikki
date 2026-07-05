using System.IO;
using System.Text.Json;

namespace FreakyNikki.Settings;

/// <summary>
/// Loads and saves <see cref="AppSettings"/> as JSON under
/// %AppData%\FreakyNikki. Writes are atomic (temp file then replace) so a crash
/// mid-write can't corrupt the file, and a missing/broken file just yields
/// defaults.
/// </summary>
public sealed class SettingsStore
{
    private readonly string _directory;
    private readonly string _filePath;

    public SettingsStore()
    {
        _directory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "FreakyNikki");
        _filePath = Path.Combine(_directory, "settings.json");
    }

    /// <summary>Full path to the settings file (also used to locate the log).</summary>
    public string Directory => _directory;

    public AppSettings Load()
    {
        try
        {
            if (!File.Exists(_filePath))
            {
                return new AppSettings();
            }

            string json = File.ReadAllText(_filePath);
            return JsonSerializer.Deserialize(json, SettingsJsonContext.Default.AppSettings)
                   ?? new AppSettings();
        }
        catch
        {
            // Unreadable/corrupt settings should never block startup.
            return new AppSettings();
        }
    }

    public void Save(AppSettings settings)
    {
        try
        {
            System.IO.Directory.CreateDirectory(_directory);
            string json = JsonSerializer.Serialize(settings, SettingsJsonContext.Default.AppSettings);

            string temp = _filePath + ".tmp";
            File.WriteAllText(temp, json);

            if (File.Exists(_filePath))
            {
                File.Replace(temp, _filePath, null);
            }
            else
            {
                File.Move(temp, _filePath);
            }
        }
        catch
        {
            // Persisting settings is best-effort; failing to save must not crash.
        }
    }
}
