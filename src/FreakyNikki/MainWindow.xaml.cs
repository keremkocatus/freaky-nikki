using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
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

    /// <summary>Commit the delay text box (and drop focus) when Enter is pressed.</summary>
    private void OnDelayKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && sender is TextBox box)
        {
            box.GetBindingExpression(TextBox.TextProperty)?.UpdateSource();
            Keyboard.ClearFocus();
            e.Handled = true;
        }
    }
}
