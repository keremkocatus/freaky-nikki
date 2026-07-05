using System.Collections.Concurrent;
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
    private readonly MMDeviceEnumerator _enumerator = new();
    private readonly ConcurrentDictionary<string, OutputChannel> _channels = new();
    private readonly Dictionary<string, OutputConfig> _configs = new();
    private readonly object _sync = new();

    private WasapiLoopbackCapture? _capture;
    private MMDevice? _captureDevice;
    private bool _disposed;

    /// <summary>Raised (on a background thread) whenever a channel's health changes.</summary>
    public event Action<string, ChannelState, string?>? ChannelStatusChanged;

    public bool IsRunning { get; private set; }

    /// <summary>ID of the current capture (default render) device, when running.</summary>
    public string? CaptureDeviceId => _captureDevice?.ID;

    /// <summary>Enable an extra device for mirroring (or update it if already enabled).</summary>
    public void EnableOutput(string deviceId, float volume, int delayMs)
    {
        lock (_sync)
        {
            _configs[deviceId] = new OutputConfig(deviceId, volume, delayMs);
            if (IsRunning)
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

            _captureDevice = _enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
            _capture = new WasapiLoopbackCapture(_captureDevice);
            _capture.DataAvailable += OnDataAvailable;
            _capture.RecordingStopped += OnRecordingStopped;
            _capture.StartRecording();
            IsRunning = true;

            foreach (OutputConfig cfg in _configs.Values.ToList())
            {
                CreateChannel(cfg.DeviceId, cfg.Volume, cfg.DelayMs);
            }
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
            if (!IsRunning)
            {
                return;
            }

            foreach (string id in _channels.Keys.ToList())
            {
                RemoveChannel(id);
            }
            StopCapture();

            _captureDevice = _enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
            _capture = new WasapiLoopbackCapture(_captureDevice);
            _capture.DataAvailable += OnDataAvailable;
            _capture.RecordingStopped += OnRecordingStopped;
            _capture.StartRecording();

            foreach (OutputConfig cfg in _configs.Values.ToList())
            {
                CreateChannel(cfg.DeviceId, cfg.Volume, cfg.DelayMs);
            }
        }
    }

    /// <summary>
    /// Re-attempt a device that previously dropped out and has now reappeared.
    /// No-op if it isn't enabled or is already running.
    /// </summary>
    public void TryReconnect(string deviceId)
    {
        lock (_sync)
        {
            if (!IsRunning || !_configs.TryGetValue(deviceId, out OutputConfig? cfg))
            {
                return;
            }
            if (_channels.ContainsKey(deviceId))
            {
                return;
            }
            CreateChannel(cfg.DeviceId, cfg.Volume, cfg.DelayMs);
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
        channel.StatusChanged += (_, e) => ChannelStatusChanged?.Invoke(deviceId, e.State, e.Message);
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
        // Capture halted unexpectedly (e.g. default device removed). If we're
        // still meant to be running, the DeviceMonitor's default-changed event
        // will drive RestartCapture(); nothing to do here beyond bookkeeping.
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
