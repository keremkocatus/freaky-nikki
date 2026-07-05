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
