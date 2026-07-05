using System.Text.Json.Serialization;

namespace FreakyNikki.Settings;

/// <summary>Persisted per-device preferences, keyed by endpoint ID.</summary>
public sealed class DeviceSetting
{
    public bool Enabled { get; set; }

    public float Volume { get; set; } = 1f;

    public int DelayMs { get; set; }

    /// <summary>Last-seen friendly name, for display when the device is offline.</summary>
    public string? Name { get; set; }
}

/// <summary>Root settings document stored at %AppData%\FreakyNikki\settings.json.</summary>
public sealed class AppSettings
{
    /// <summary>Device endpoint ID → saved preferences.</summary>
    public Dictionary<string, DeviceSetting> Devices { get; set; } = new();

    /// <summary>Start hidden to the tray instead of showing the window.</summary>
    public bool StartMinimized { get; set; }
}

/// <summary>Source-generated JSON context (trim/AOT friendly).</summary>
[JsonSourceGenerationOptions(WriteIndented = true)]
[JsonSerializable(typeof(AppSettings))]
internal sealed partial class SettingsJsonContext : JsonSerializerContext;
