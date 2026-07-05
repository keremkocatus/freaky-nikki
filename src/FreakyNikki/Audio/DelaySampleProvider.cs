using System.Threading;
using NAudio.Wave;

namespace FreakyNikki.Audio;

/// <summary>
/// Adds an adjustable playback delay in front of a source, and guarantees that
/// <see cref="Read"/> always returns the full requested count by padding with
/// silence. That second property doubles as the engine's "silence pump": the
/// WASAPI render client is always fed, so it never underruns/stops when the
/// captured audio goes quiet.
/// </summary>
/// <remarks>
/// The delay is realised as leading silence. Increasing the delay prepends more
/// silence (the device lags further behind); decreasing it drops already-buffered
/// source samples to catch up. Both are safe to do while playing — the target is
/// read atomically on the render thread.
/// </remarks>
public sealed class DelaySampleProvider : ISampleProvider
{
    private const int MaxDelayMs = 2000;

    private readonly ISampleProvider _source;
    private readonly int _channels;
    private readonly int _sampleRate;

    private long _targetDelaySamples;   // total samples (frame-aligned), read atomically
    private long _emittedSilence;        // silence already prepended, in total samples
    private float[] _skipBuffer = new float[4096];

    public DelaySampleProvider(ISampleProvider source)
    {
        _source = source;
        _channels = source.WaveFormat.Channels;
        _sampleRate = source.WaveFormat.SampleRate;
    }

    public WaveFormat WaveFormat => _source.WaveFormat;

    /// <summary>Delay in milliseconds (0–2000). Safe to set while playing.</summary>
    public int DelayMs
    {
        get
        {
            long samples = Interlocked.Read(ref _targetDelaySamples);
            return (int)(samples * 1000 / ((long)_sampleRate * _channels));
        }
        set
        {
            int ms = Math.Clamp(value, 0, MaxDelayMs);
            long samples = (long)ms * _sampleRate / 1000 * _channels; // frame-aligned
            Interlocked.Exchange(ref _targetDelaySamples, samples);
        }
    }

    public int Read(float[] buffer, int offset, int count)
    {
        long target = Interlocked.Read(ref _targetDelaySamples);
        int written = 0;

        if (_emittedSilence < target)
        {
            // Growing the delay: emit leading silence (frame-aligned by construction).
            int silence = (int)Math.Min(count, target - _emittedSilence);
            Array.Clear(buffer, offset, silence);
            _emittedSilence += silence;
            written += silence;
            if (written == count)
            {
                return count;
            }
        }
        else if (_emittedSilence > target)
        {
            // Shrinking the delay: discard the excess from the source to catch up.
            Skip(_emittedSilence - target);
            _emittedSilence = target;
        }

        int got = _source.Read(buffer, offset + written, count - written);
        written += got;

        // Silence pump: never hand the render client a short buffer.
        if (written < count)
        {
            Array.Clear(buffer, offset + written, count - written);
            written = count;
        }

        return written;
    }

    private void Skip(long samples)
    {
        while (samples > 0)
        {
            int chunk = (int)Math.Min(samples, _skipBuffer.Length);
            int read = _source.Read(_skipBuffer, 0, chunk);
            if (read == 0)
            {
                break;
            }
            samples -= read;
        }
    }
}
