using NAudio.CoreAudioApi;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;

namespace FreakyNikki.Audio;

/// <summary>
/// One extra output device. Owns the signal chain that turns the captured
/// system audio into something the device can play:
/// <c>buffer → drift-guard → channel-match → resample → volume → delay → WASAPI</c>.
/// The captured audio is written in via <see cref="AddSamples"/>; a dedicated
/// WASAPI render thread pulls it back out through the chain.
/// </summary>
public sealed class OutputChannel : IDisposable
{
    // Shared-mode render latency. ~150 ms is a safe starting point for Bluetooth.
    private const int OutputLatencyMs = 150;

    // How much captured audio we buffer before playing, and the backlog ceiling
    // the drift guard trims back to.
    private const int BufferDurationMs = 800;
    private const double MaxBufferedMs = 200;

    private readonly MMDevice _device;
    private readonly WaveFormat _captureFormat;
    private readonly BufferedWaveProvider _buffer;
    private readonly VolumeSampleProvider _volume;
    private readonly DelaySampleProvider _delay;

    private WasapiOut? _output;
    private volatile bool _running;
    private bool _disposed;

    public OutputChannel(MMDevice device, WaveFormat captureFormat, float volume, int delayMs)
    {
        _device = device;
        _captureFormat = captureFormat;
        DeviceId = device.ID;
        DeviceName = device.FriendlyName;

        _buffer = new BufferedWaveProvider(captureFormat)
        {
            DiscardOnBufferOverflow = true,
            ReadFully = true,
            BufferDuration = TimeSpan.FromMilliseconds(BufferDurationMs),
        };

        // Build the chain to match the device's shared-mode mix format exactly,
        // so WASAPI doesn't have to convert (and won't reject the format).
        WaveFormat mix = device.AudioClient.MixFormat;

        ISampleProvider chain = _buffer.ToSampleProvider();
        chain = new DriftSampleProvider(chain, () => _buffer.BufferedDuration.TotalMilliseconds, MaxBufferedMs);
        chain = MatchChannels(chain, mix.Channels);
        if (chain.WaveFormat.SampleRate != mix.SampleRate)
        {
            chain = new WdlResamplingSampleProvider(chain, mix.SampleRate);
        }

        _volume = new VolumeSampleProvider(chain) { Volume = Math.Clamp(volume, 0f, 1f) };
        _delay = new DelaySampleProvider(_volume) { DelayMs = delayMs };
    }

    public string DeviceId { get; }

    public string DeviceName { get; }

    public ChannelState State { get; private set; } = ChannelState.Idle;

    public event EventHandler<ChannelStatusEventArgs>? StatusChanged;

    /// <summary>Per-device volume, 0.0–1.0. Safe to set while playing.</summary>
    public float Volume
    {
        get => _volume.Volume;
        set => _volume.Volume = Math.Clamp(value, 0f, 1f);
    }

    /// <summary>Per-device delay in ms, 0–2000. Safe to set while playing.</summary>
    public int DelayMs
    {
        get => _delay.DelayMs;
        set => _delay.DelayMs = value;
    }

    /// <summary>Feed a block of captured audio (in the capture format) to this device.</summary>
    public void AddSamples(byte[] data, int offset, int count)
    {
        if (_running)
        {
            _buffer.AddSamples(data, offset, count);
        }
    }

    /// <summary>Open the device and begin playback. Idempotent.</summary>
    public void Start()
    {
        if (_running || _disposed)
        {
            return;
        }

        SetState(ChannelState.Starting);
        try
        {
            _buffer.ClearBuffer();
            _output = new WasapiOut(_device, AudioClientShareMode.Shared, useEventSync: true, OutputLatencyMs);
            _output.PlaybackStopped += OnPlaybackStopped;
            _output.Init(_delay.ToWaveProvider());
            _running = true;
            _output.Play();
            SetState(ChannelState.Playing);
        }
        catch (Exception ex)
        {
            _running = false;
            DisposeOutput();
            SetState(ChannelState.Error, ex.Message);
        }
    }

    /// <summary>Stop playback and release the device, keeping settings intact.</summary>
    public void Stop()
    {
        if (!_running && _output is null)
        {
            return;
        }

        _running = false;
        DisposeOutput();
        SetState(ChannelState.Stopped);
    }

    private void OnPlaybackStopped(object? sender, StoppedEventArgs e)
    {
        if (!_running)
        {
            return; // expected stop
        }

        _running = false;
        // Unexpected stop while we thought we were playing — usually the device
        // was unplugged. Mark it reconnecting; the engine re-adds it when it returns.
        SetState(ChannelState.Reconnecting, e.Exception?.Message);
    }

    private void SetState(ChannelState state, string? message = null)
    {
        State = state;
        StatusChanged?.Invoke(this, new ChannelStatusEventArgs(state, message));
    }

    private void DisposeOutput()
    {
        if (_output is not null)
        {
            _output.PlaybackStopped -= OnPlaybackStopped;
            try
            {
                _output.Dispose();
            }
            catch
            {
                // Disposing a device that already vanished can throw; ignore.
            }
            _output = null;
        }
    }

    private static ISampleProvider MatchChannels(ISampleProvider source, int targetChannels)
    {
        int channels = source.WaveFormat.Channels;
        if (channels == targetChannels)
        {
            return source;
        }
        if (channels == 2 && targetChannels == 1)
        {
            return new StereoToMonoSampleProvider(source);
        }
        if (channels == 1 && targetChannels == 2)
        {
            return new MonoToStereoSampleProvider(source);
        }

        // Uncommon layout (e.g. 5.1). Leave as-is; WASAPI Init will report the
        // failure through the Error state rather than crashing.
        return source;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }
        _disposed = true;
        _running = false;
        DisposeOutput();
    }
}
