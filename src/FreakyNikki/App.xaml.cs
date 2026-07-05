using System.Windows;
using FreakyNikki.Audio;
using FreakyNikki.Settings;
using FreakyNikki.Theme;
using FreakyNikki.ViewModels;

namespace FreakyNikki;

/// <summary>
/// Application entry point. Wires up theming, the audio engine, device monitor
/// and the main view model, then shows the window.
/// </summary>
public partial class App : System.Windows.Application
{
    private ThemeManager? _theme;
    private AudioEngine? _engine;
    private DeviceMonitor? _monitor;
    private MainViewModel? _viewModel;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        var store = new SettingsStore();
        AppSettings settings = store.Load();

        _theme = new ThemeManager(this);
        _theme.Apply();

        _engine = new AudioEngine();
        _monitor = new DeviceMonitor();
        _viewModel = new MainViewModel(_engine, _monitor, store, settings);

        var window = new MainWindow(_theme) { DataContext = _viewModel };
        MainWindow = window;
        window.Show();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _viewModel?.Dispose();
        _engine?.Dispose();
        _monitor?.Dispose();
        _theme?.Dispose();
        base.OnExit(e);
    }
}
