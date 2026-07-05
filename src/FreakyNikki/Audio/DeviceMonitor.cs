using NAudio.CoreAudioApi;
using NAudio.CoreAudioApi.Interfaces;

namespace FreakyNikki.Audio;

/// <summary>
/// Enumerates active render devices and watches for hot-plug / default-device
/// changes via <see cref="IMMNotificationClient"/>. Callbacks arrive on a system
/// thread, so <see cref="DevicesChanged"/> and <see cref="DefaultChanged"/> are
/// raised off the UI thread — subscribers must marshal to the dispatcher.
/// </summary>
public sealed class DeviceMonitor : IMMNotificationClient, IDisposable
{
    // PKEY_Device_EnumeratorName — tells us the bus a device sits on ("BTHENUM"
    // for Bluetooth), used to pick a sensible default delay.
    private static readonly Guid EnumeratorNameKey = new("A45C254E-DF1C-4EFD-8020-67D146A850E0");
    private const int EnumeratorNamePropertyId = 24;

    private readonly MMDeviceEnumerator _enumerator = new();
    private bool _registered;
    private bool _disposed;

    public DeviceMonitor()
    {
        _enumerator.RegisterEndpointNotificationCallback(this);
        _registered = true;
    }

    /// <summary>The device set changed (added/removed/enabled/disabled).</summary>
    public event Action? DevicesChanged;

    /// <summary>The default render device changed; argument is its device ID.</summary>
    public event Action<string>? DefaultChanged;

    /// <summary>Snapshot of currently active render devices, default first.</summary>
    public IReadOnlyList<DeviceInfo> GetRenderDevices()
    {
        string? defaultId = GetDefaultRenderId();
        var devices = new List<DeviceInfo>();

        foreach (MMDevice device in _enumerator.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active))
        {
            using (device)
            {
                devices.Add(new DeviceInfo(
                    device.ID,
                    device.FriendlyName,
                    device.ID == defaultId,
                    IsBluetooth(device)));
            }
        }

        return devices
            .OrderByDescending(d => d.IsDefault)
            .ThenBy(d => d.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }

    public string? GetDefaultRenderId()
    {
        try
        {
            if (!_enumerator.HasDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia))
            {
                return null;
            }
            using MMDevice device = _enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
            return device.ID;
        }
        catch
        {
            return null;
        }
    }

    private static bool IsBluetooth(MMDevice device)
    {
        try
        {
            PropertyStore props = device.Properties;
            for (int i = 0; i < props.Count; i++)
            {
                PropertyStoreProperty p = props[i];
                if (p.Key.formatId == EnumeratorNameKey && p.Key.propertyId == EnumeratorNamePropertyId)
                {
                    return p.Value is string name
                        && name.Contains("BTH", StringComparison.OrdinalIgnoreCase);
                }
            }
        }
        catch
        {
            // Some endpoints don't expose the property store; fall back to the name.
        }

        return device.FriendlyName.Contains("Bluetooth", StringComparison.OrdinalIgnoreCase);
    }

    // --- IMMNotificationClient (called on a system thread) ---

    public void OnDeviceStateChanged(string deviceId, DeviceState newState) => DevicesChanged?.Invoke();

    public void OnDeviceAdded(string pwstrDeviceId) => DevicesChanged?.Invoke();

    public void OnDeviceRemoved(string deviceId) => DevicesChanged?.Invoke();

    public void OnDefaultDeviceChanged(DataFlow flow, Role role, string defaultDeviceId)
    {
        if (flow == DataFlow.Render && role == Role.Multimedia)
        {
            DefaultChanged?.Invoke(defaultDeviceId);
        }
    }

    public void OnPropertyValueChanged(string pwstrDeviceId, PropertyKey key)
    {
        // Friendly-name edits etc. — cheap to just refresh.
        DevicesChanged?.Invoke();
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }
        _disposed = true;
        if (_registered)
        {
            try
            {
                _enumerator.UnregisterEndpointNotificationCallback(this);
            }
            catch
            {
                // Ignore teardown races.
            }
        }
        _enumerator.Dispose();
    }
}
