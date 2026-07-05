namespace FreakyNikki.Audio;

/// <summary>
/// Lifecycle/health state of a single <see cref="OutputChannel"/>, surfaced to
/// the UI as a coloured status dot.
/// </summary>
public enum ChannelState
{
    /// <summary>Configured but not currently playing.</summary>
    Idle,

    /// <summary>Opening the device and starting playback.</summary>
    Starting,

    /// <summary>Actively mirroring audio to the device.</summary>
    Playing,

    /// <summary>Device dropped out; waiting for it to return.</summary>
    Reconnecting,

    /// <summary>Playback failed (e.g. device in use, access denied).</summary>
    Error,

    /// <summary>Cleanly stopped.</summary>
    Stopped,
}

/// <summary>Payload for <see cref="OutputChannel.StatusChanged"/>.</summary>
public sealed class ChannelStatusEventArgs(ChannelState state, string? message = null) : EventArgs
{
    public ChannelState State { get; } = state;

    /// <summary>Human-readable detail for the <see cref="ChannelState.Error"/> state.</summary>
    public string? Message { get; } = message;
}
