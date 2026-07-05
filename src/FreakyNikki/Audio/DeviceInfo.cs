namespace FreakyNikki.Audio;

/// <summary>Immutable snapshot of a render endpoint for the UI.</summary>
public sealed record DeviceInfo(string Id, string Name, bool IsDefault);
