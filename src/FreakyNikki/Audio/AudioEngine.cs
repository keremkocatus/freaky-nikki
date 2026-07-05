using System.Collections.Concurrent;
using System.Threading;
using Microsoft.Win32;
using NAudio.CoreAudioApi;
using NAudio.Wave;

namespace FreakyNikki.Audio;

/// <summary>
/// Heart of the app. Captures the default render device via WASAPI loopback and
/// fans every captured buffer out to the enabled <see cref="OutputChannel"/>s.
/// The default device itself is never mirrored — it keeps playing natively with
/// zero added latency; we only copy its sound onto the extra devices.
/// </summary>
public sealed class AudioEngine : IDisposable
{
    // Watchdog cadence — how often we heal a dead capture or stalled outputs.
    // This is what keeps long sessions (movies) alive across sleep/resume, a
    // silent Bluetooth stall, or a device that quietly dropped and came back.
    private const int WatchdogIntervalMs = 4000;

    private readonly MMDeviceEnumerator _enumerator = new();
    private readonly ConcurrentDictionary<string, OutputChannel> _channels = new();
    private readonly Dictionary<string, OutputConfig> _configs = new();
    private readonly object _sync = new();

    private WasapiLoopbackCapture? _capture;
    private MMDevice? _captureDevice;
    private Timer? _watchdog;
    private volatile bool _captureFaulted;
    private bool _disposed;

    public AudioEngine()
    {
        // Audio devices are dead after the machine resumes from sleep; flag a
        // rebuild so the watchdog brings everything back.
        SystemEvents.PowerModeChanged += OnPowerModeChanged;
    }

    /// <summary>Raised (on a background thread) whenever a channel's health changes.</summary>
    public event Action<string, ChannelState, string?>? ChannelStatusChanged;

    public bool IsRunning { get; private set; }

    /// <summary>ID of the current capture (default render) device, when running.</summary>
    public string? CaptureDeviceId => _captureDevice?.ID;

    /// <summary>
    /// Enable an extra device for mirroring, or update it if already enabled.
    /// Idempotent: a healthy channel is adjusted in place (no glitch), while a
    /// missing or dropped one is (re)created — which is also how a device that
    /// came back reconnects.
    /// </summary>
    public void EnableOutput(string deviceId, float volume, int delayMs)
    {
        lock (_sync)
        {
            _configs[deviceId] = new OutputConfig(deviceId, volume, delayMs);
            if (!IsRunning)
            {
                return;
            }

            if (_channels.TryGetValue(deviceId, out OutputChannel? channel)
                && channel.State is ChannelState.Playing or ChannelState.Starting)
            {
                channel.Volume = volume;
                channel.DelayMs = delayMs;
            }
            else
            {
                CreateChannel(deviceId, volume, delayMs);
            }
        }
    }

    /// <summary>Stop mirroring to a device and forget it.</summary>
    public void DisableOutput(string deviceId)
    {
        lock (_sync)
        {
            _configs.Remove(deviceId);
            RemoveChannel(deviceId);
        }
    }

    public void SetVolume(string deviceId, float volume)
    {
        lock (_sync)
        {
            if (_configs.TryGetValue(deviceId, out OutputConfig? cfg))
            {
                cfg.Volume = volume;
            }
        }
        if (_channels.TryGetValue(deviceId, out OutputChannel? channel))
        {
            channel.Volume = volume;
        }
    }

    public void SetDelay(string deviceId, int delayMs)
    {
        lock (_sync)
        {
            if (_configs.TryGetValue(deviceId, out OutputConfig? cfg))
            {
                cfg.DelayMs = delayMs;
            }
        }
        if (_channels.TryGetValue(deviceId, out OutputChannel? channel))
        {
            channel.DelayMs = delayMs;
        }
    }

    /// <summary>Start loopback capture and every enabled output.</summary>
    public void Start()
    {
        lock (_sync)
        {
            if (IsRunning || _disposed)
            {
                return;
            }

            try
            {
                BuildCaptureLocked();
                IsRunning = true;
            }
            catch (Exception ex)
            {
                Diagnostics.Log.Error("Failed to start loopback capture", ex);
                StopCapture();
                return;
            }

            foreach (OutputConfig cfg in _configs.Values.ToList())
            {
                CreateChannel(cfg.DeviceId, cfg.Volume, cfg.DelayMs);
            }

            _captureFaulted = false;
            _watchdog = new Timer(OnWatchdog, null, WatchdogIntervalMs, WatchdogIntervalMs);
        }
    }

    /// <summary>Stop every output and the capture.</summary>
    public void Stop()
    {
        lock (_sync)
        {
            if (!IsRunning)
            {
                return;
            }
            IsRunning = false;

            _watchdog?.Dispose();
            _watchdog = null;

            foreach (string id in _channels.Keys.ToList())
            {
                RemoveChannel(id);
            }

            StopCapture();
        }
    }

    /// <summary>
    /// Rebuild capture on the (possibly new) default device without disturbing
    /// the user's enabled outputs. Called when Windows switches default device.
    /// </summary>
    public void RestartCapture()
    {
        lock (_sync)
        {
            if (IsRunning)
            {
                RebuildAllLocked();
            }
        }
    }

    // --- internals (all channel/capture mutation happens under _sync) ---

    private void CreateChannel(string deviceId, float volume, int delayMs)
    {
        if (_capture is null || deviceId == _captureDevice?.ID)
        {
            return; // never mirror the default device onto itself
        }

        RemoveChannel(deviceId);

        MMDevice? device = TryGetDevice(deviceId);
        if (device is null)
        {
            ChannelStatusChanged?.Invoke(deviceId, ChannelState.Reconnecting, "Device not available");
            return;
        }

        var channel = new OutputChannel(device, _capture.WaveFormat, volume, delayMs);
        channel.StatusChanged += (_, e) =>
        {
            if (e.State == ChannelState.Error)
            {
                Diagnostics.Log.Error($"Channel '{channel.DeviceName}' failed: {e.Message}");
            }
            ChannelStatusChanged?.Invoke(deviceId, e.State, e.Message);
        };
        _channels[deviceId] = channel;
        channel.Start();
    }

    private void RemoveChannel(string deviceId)
    {
        if (_channels.TryRemove(deviceId, out OutputChannel? channel))
        {
            channel.Stop();
            channel.Dispose();
        }
    }

    private MMDevice? TryGetDevice(string deviceId)
    {
        try
        {
            MMDevice device = _enumerator.GetDevice(deviceId);
            return device.State == DeviceState.Active ? device : null;
        }
        catch
        {
            return null;
        }
    }

    private void OnDataAvailable(object? sender, WaveInEventArgs e)
    {
        foreach (OutputChannel channel in _channels.Values)
        {
            channel.AddSamples(e.Buffer, 0, e.BytesRecorded);
        }
    }

    private void OnRecordingStopped(object? sender, StoppedEventArgs e)
    {
        // Capture halted unexpectedly (device removed, sleep/resume, driver
        // glitch). Flag it; the watchdog rebuilds capture on its next tick.
        if (IsRunning)
        {
            Diagnostics.Log.Warn($"Loopback capture stopped unexpectedly: {e.Exception?.Message}");
            _captureFaulted = true;
        }
    }

    private void BuildCaptureLocked()
    {
        _captureDevice = _enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
        _capture = new WasapiLoopbackCapture(_captureDevice);
        _capture.DataAvailable += OnDataAvailable;
        _capture.RecordingStopped += OnRecordingStopped;
        _capture.StartRecording();
    }

    /// <summary>Tear down and rebuild capture and all channels. Caller holds <c>_sync</c>.</summary>
    private void RebuildAllLocked()
    {
        foreach (string id in _channels.Keys.ToList())
        {
            RemoveChannel(id);
        }
        StopCapture();

        try
        {
            BuildCaptureLocked();
        }
        catch (Exception ex)
        {
            // No default device yet (e.g. mid-resume) — leave faulted so the
            // watchdog retries on its next tick.
            Diagnostics.Log.Error("Capture rebuild failed; will retry", ex);
            StopCapture();
            _captureFaulted = true;
            return;
        }

        foreach (OutputConfig cfg in _configs.Values.ToList())
        {
            CreateChannel(cfg.DeviceId, cfg.Volume, cfg.DelayMs);
        }
        _captureFaulted = false;
    }

    private void OnWatchdog(object? state)
    {
        if (_disposed || !Monitor.TryEnter(_sync))
        {
            return; // never let the watchdog pile up behind a busy lock
        }

        try
        {
            if (!IsRunning)
            {
                return;
            }

            if (_captureFaulted || _capture is null)
            {
                RebuildAllLocked();
                return;
            }

            // Revive any enabled output that isn't currently healthy — covers a
            // silent stall or a device that dropped without a system notification.
            foreach (OutputConfig cfg in _configs.Values.ToList())
            {
                if (cfg.DeviceId == _captureDevice?.ID)
                {
                    continue;
                }

                bool healthy = _channels.TryGetValue(cfg.DeviceId, out OutputChannel? channel)
                    && channel.State is ChannelState.Playing or ChannelState.Starting;
                if (!healthy)
                {
                    CreateChannel(cfg.DeviceId, cfg.Volume, cfg.DelayMs);
                }
            }
        }
        catch (Exception ex)
        {
            Diagnostics.Log.Error("Watchdog error", ex);
        }
        finally
        {
            Monitor.Exit(_sync);
        }
    }

    private void OnPowerModeChanged(object? sender, PowerModeChangedEventArgs e)
    {
        if (e.Mode == PowerModes.Resume)
        {
            _captureFaulted = true;
        }
    }

    private void StopCapture()
    {
        if (_capture is not null)
        {
            _capture.DataAvailable -= OnDataAvailable;
            _capture.RecordingStopped -= OnRecordingStopped;
            try
            {
                _capture.StopRecording();
                _capture.Dispose();
            }
            catch
            {
                // Device already gone.
            }
            _capture = null;
        }
        _captureDevice = null;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }
        _disposed = true;
        SystemEvents.PowerModeChanged -= OnPowerModeChanged;
        Stop();
        _enumerator.Dispose();
    }

    private sealed class OutputConfig(string deviceId, float volume, int delayMs)
    {
        public string DeviceId { get; } = deviceId;

        public float Volume { get; set; } = volume;

        public int DelayMs { get; set; } = delayMs;
    }
}
