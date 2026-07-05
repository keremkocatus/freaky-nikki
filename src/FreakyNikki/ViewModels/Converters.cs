using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;
using FreakyNikki.Audio;

namespace FreakyNikki.ViewModels;

/// <summary>Maps a <see cref="ChannelState"/> to its status-dot brush.</summary>
public sealed class StateToBrushConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        string key = value switch
        {
            ChannelState.Playing => "Brush.StatusPlaying",
            ChannelState.Reconnecting or ChannelState.Starting => "Brush.StatusReconnecting",
            ChannelState.Error => "Brush.StatusError",
            _ => "Brush.StatusIdle",
        };
        return Application.Current.TryFindResource(key) as Brush ?? Brushes.Gray;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>Formats a delay in ms as a short label, e.g. "120 ms".</summary>
public sealed class DelayLabelConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => $"{System.Convert.ToInt32(value)} ms";

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>Inverts a boolean (for enabling controls when NOT running, etc.).</summary>
public sealed class InverseBoolConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is bool b && !b;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is bool b && !b;
}
