using System.Windows;
using FreakyNikki.Theme;

namespace FreakyNikki;

/// <summary>The single application window.</summary>
public partial class MainWindow : Window
{
    private readonly ThemeManager _theme;

    public MainWindow(ThemeManager theme)
    {
        _theme = theme;
        InitializeComponent();

        // The window handle exists only after SourceInitialized — apply the dark
        // title bar there, and again whenever the system theme flips.
        SourceInitialized += (_, _) => _theme.ApplyWindowChrome(this);
        _theme.ThemeChanged += () => _theme.ApplyWindowChrome(this);
    }
}
