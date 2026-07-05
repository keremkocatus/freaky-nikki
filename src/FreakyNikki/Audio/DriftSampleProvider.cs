using NAudio.Wave;

namespace FreakyNikki.Audio;

/// <summary>
/// Lightweight clock-drift guard. The capture clock (default device) and each
/// output device's clock are never perfectly equal, so over time an output's
/// backlog slowly grows. Left alone that means ever-increasing latency and,
/// eventually, dropped buffers. When the measured backlog exceeds a ceiling this
/// provider discards a small, frame-aligned slice from the source to catch up —
/// an occasional, barely audible skip instead of a slow desync.
/// </summary>
public sealed class DriftSampleProvider : ISampleProvider
{
    private readonly ISampleProvider _source;
    private readonly Func<double> _bufferedMs;
    private readonly double _maxBufferedMs;
    private readonly int _channels;
    private readonly int _maxDropPerRead;
    private readonly float[] _discard = new float[8192];

    /// <param name="source">Upstream provider (capture-rate domain).</param>
    /// <param name="bufferedMs">Live backlog of the feeding buffer, in ms.</param>
    /// <param name="maxBufferedMs">Backlog ceiling before trimming kicks in.</param>
    public DriftSampleProvider(ISampleProvider source, Func<double> bufferedMs, double maxBufferedMs)
    {
        _source = source;
        _bufferedMs = bufferedMs;
        _maxBufferedMs = maxBufferedMs;
        _channels = source.WaveFormat.Channels;
        // Trim at most ~5 ms per read so corrections stay inaudible.
        int perRead = source.WaveFormat.SampleRate * 5 / 1000 * _channels;
        _maxDropPerRead = Math.Max(_channels, perRead - (perRead % _channels));
    }

    public WaveFormat WaveFormat => _source.WaveFormat;

    public int Read(float[] buffer, int offset, int count)
    {
        int read = _source.Read(buffer, offset, count);

        if (_bufferedMs() > _maxBufferedMs)
        {
            int dropped = 0;
            while (dropped < _maxDropPerRead)
            {
                int chunk = Math.Min(_maxDropPerRead - dropped, _discard.Length);
                int d = _source.Read(_discard, 0, chunk);
                if (d == 0)
                {
                    break;
                }
                dropped += d;
            }
        }

        return read;
    }
}
