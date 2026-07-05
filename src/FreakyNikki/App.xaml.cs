using System.ComponentModel;
using System.Threading;
using System.Windows;
using System.Windows.Threading;
using FreakyNikki.Audio;
using FreakyNikki.Diagnostics;
using FreakyNikki.Settings;
using FreakyNikki.Theme;
using FreakyNikki.Tray;
using FreakyNikki.ViewModels;

namespace FreakyNikki;

/// <summary>
/// Application entry point: enforces a single instance, wires theming, audio and
/// the tray icon together, and keeps the app alive in the tray when the window
/// is closed.
/// </summary>
public partial class App : System.Windows.Application
{
    private const string MutexName = "FreakyNikki.SingleInstance.v1";
    private const string ShowEventName = "FreakyNikki.ShowWindow.v1";

    private Mutex? _mutex;
    private EventWaitHandle? _showEvent;
    private ThemeManager? _theme;
    private AudioEngine? _engine;
    private DeviceMonitor? _monitor;
    private MainViewModel? _viewModel;
    private TrayIcon? _tray;
    private MainWindow? _window;
    private bool _exiting;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        _mutex = new Mutex(true, MutexName, out bool isNew);
        if (!isNew)
        {
            // Another instance is running — ask it to surface, then bow out.
            try
            {
                EventWaitHandle.OpenExisting(ShowEventName).Set();
            }
            catch
            {
                // The other instance may be mid-startup; nothing else to do.
            }
            Shutdown();
            return;
        }

        var store = new SettingsStore();
        Log.Init(store.Directory);
        Log.Info("Starting Freaky Nikki");

        DispatcherUnhandledException += OnUnhandledException;

        AppSettings settings = store.Load();

        _theme = new ThemeManager(this);
        _theme.Apply();

        _engine = new AudioEngine();
        _monitor = new DeviceMonitor();
        _viewModel = new MainViewModel(_engine, _monitor, store, settings);
        _viewModel.PropertyChanged += OnViewModelPropertyChanged;

        _tray = new TrayIcon();
        _tray.OpenRequested += ShowMainWindow;
        _tray.ToggleRequested += () => _viewModel.ToggleRun();
        _tray.QuitRequested += QuitApplication;

        _window = new MainWindow(_theme) { DataContext = _viewModel };
        _window.Closing += OnWindowClosing;
        MainWindow = _window;

        if (!settings.StartMinimized)
        {
            _window.Show();
        }

        StartShowListener();
    }

    private void StartShowListener()
    {
        _showEvent = new EventWaitHandle(false, EventResetMode.AutoReset, ShowEventName);
        var thread = new Thread(() =>
        {
            while (_showEvent.WaitOne())
            {
                Dispatcher.BeginInvoke(ShowMainWindow);
            }
        })
        {
            IsBackground = true,
            Name = "ShowListener",
        };
        thread.Start();
    }

    private void ShowMainWindow()
    {
        if (_window is null)
        {
            return;
        }
        _window.Show();
        if (_window.WindowState == WindowState.Minimized)
        {
            _window.WindowState = WindowState.Normal;
        }
        _window.Activate();
        _window.Topmost = true;
        _window.Topmost = false;
    }

    private void OnWindowClosing(object? sender, CancelEventArgs e)
    {
        if (_exiting)
        {
            return;
        }
        // Closing the window just tucks the app into the tray.
        e.Cancel = true;
        _window?.Hide();
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MainViewModel.IsRunning))
        {
            _tray?.SetRunning(_viewModel!.IsRunning);
        }
    }

    private void QuitApplication()
    {
        _exiting = true;
        Shutdown();
    }

    private void OnUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        Log.Error("Unhandled UI exception", e.Exception);
        e.Handled = true;
        MessageBox.Show(
            "Something went wrong. The app will keep running.\n\n" + e.Exception.Message,
            "Freaky Nikki",
            MessageBoxButton.OK,
            MessageBoxImage.Warning);
    }

    protected override void OnExit(ExitEventArgs e)
    {
        Log.Info("Exiting");
        _viewModel?.Dispose();
        _engine?.Dispose();
        _monitor?.Dispose();
        _theme?.Dispose();
        _tray?.Dispose();
        _mutex?.Dispose();
        base.OnExit(e);
    }
}
