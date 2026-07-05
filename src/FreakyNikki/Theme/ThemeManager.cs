using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using Microsoft.Win32;
using Application = System.Windows.Application;
using Color = System.Windows.Media.Color;

namespace FreakyNikki.Theme;

/// <summary>
/// Makes the app follow the Windows theme: it loads the light or dark colour set,
/// pulls in the system accent colour, and keeps both in sync live when the user
/// flips their theme or accent. Also applies the immersive dark title bar.
/// </summary>
public sealed class ThemeManager : IDisposable
{
    private const string PersonalizeKey = @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize";
    private const string DwmKey = @"Software\Microsoft\Windows\DWM";
    private const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;

    private static readonly Color FallbackAccent = Color.FromRgb(0x63, 0x66, 0xF1);

    private readonly Application _app;
    private ResourceDictionary? _colors;
    private bool _disposed;

    public ThemeManager(Application app)
    {
        _app = app;
        SystemEvents.UserPreferenceChanged += OnUserPreferenceChanged;
    }

    /// <summary>Raised (on the UI thread) after the theme is re-applied.</summary>
    public event Action? ThemeChanged;

    public bool IsDark { get; private set; }

    /// <summary>Load the current system theme and accent into application resources.</summary>
    public void Apply()
    {
        IsDark = IsSystemDark();
        LoadColors(IsDark);
        InjectAccent(IsDark);
    }

    /// <summary>Apply the light/dark title bar to a window (call once its handle exists).</summary>
    public void ApplyWindowChrome(Window window)
    {
        IntPtr hwnd = new WindowInteropHelper(window).Handle;
        if (hwnd == IntPtr.Zero)
        {
            return;
        }

        int useDark = IsDark ? 1 : 0;
        try
        {
            DwmSetWindowAttribute(hwnd, DWMWA_USE_IMMERSIVE_DARK_MODE, ref useDark, sizeof(int));
        }
        catch
        {
            // Older Windows without the attribute — cosmetic only, ignore.
        }
    }

    private void LoadColors(bool dark)
    {
        string name = dark ? "Dark" : "Light";
        var dict = new ResourceDictionary
        {
            Source = new Uri($"pack://application:,,,/Theme/Colors.{name}.xaml", UriKind.Absolute),
        };

        var merged = _app.Resources.MergedDictionaries;
        if (_colors is not null)
        {
            merged.Remove(_colors);
        }
        merged.Insert(0, dict);
        _colors = dict;
    }

    private void InjectAccent(bool dark)
    {
        Color accent = GetSystemAccent() ?? FallbackAccent;
        Color hover = dark ? Lighten(accent, 0.14) : Darken(accent, 0.14);
        Color onAccent = Luminance(accent) > 0.62 ? Color.FromRgb(0x1A, 0x1C, 0x1E) : Colors.White;

        // Direct App.Resources entries win over the merged colour dictionary.
        _app.Resources["Color.Accent"] = accent;
        _app.Resources["Color.AccentHover"] = hover;
        _app.Resources["Brush.Accent"] = new SolidColorBrush(accent);
        _app.Resources["Brush.AccentHover"] = new SolidColorBrush(hover);
        _app.Resources["Brush.OnAccent"] = new SolidColorBrush(onAccent);
    }

    private void OnUserPreferenceChanged(object? sender, UserPreferenceChangedEventArgs e)
    {
        if (e.Category is UserPreferenceCategory.General or UserPreferenceCategory.Color)
        {
            _app.Dispatcher.BeginInvoke(() =>
            {
                Apply();
                ThemeChanged?.Invoke();
            });
        }
    }

    private static bool IsSystemDark()
    {
        try
        {
            using RegistryKey? key = Registry.CurrentUser.OpenSubKey(PersonalizeKey);
            return key?.GetValue("AppsUseLightTheme") is int light && light == 0;
        }
        catch
        {
            return false;
        }
    }

    private static Color? GetSystemAccent()
    {
        try
        {
            using RegistryKey? key = Registry.CurrentUser.OpenSubKey(DwmKey);
            // Stored as a 0xAABBGGRR DWORD.
            if (key?.GetValue("AccentColor") is int dword)
            {
                byte r = (byte)(dword & 0xFF);
                byte g = (byte)((dword >> 8) & 0xFF);
                byte b = (byte)((dword >> 16) & 0xFF);
                return Color.FromRgb(r, g, b);
            }
        }
        catch
        {
            // Fall through to default accent.
        }
        return null;
    }

    private static Color Lighten(Color c, double amount) => Color.FromRgb(
        (byte)(c.R + (255 - c.R) * amount),
        (byte)(c.G + (255 - c.G) * amount),
        (byte)(c.B + (255 - c.B) * amount));

    private static Color Darken(Color c, double amount) => Color.FromRgb(
        (byte)(c.R * (1 - amount)),
        (byte)(c.G * (1 - amount)),
        (byte)(c.B * (1 - amount)));

    private static double Luminance(Color c) => (0.299 * c.R + 0.587 * c.G + 0.114 * c.B) / 255.0;

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }
        _disposed = true;
        SystemEvents.UserPreferenceChanged -= OnUserPreferenceChanged;
    }

    [DllImport("dwmapi.dll", PreserveSig = true)]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);
}
