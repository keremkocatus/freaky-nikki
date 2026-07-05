using System.Collections.ObjectModel;
using System.Windows.Threading;
using FreakyNikki.Audio;
using FreakyNikki.Settings;
using FreakyNikki.Update;

namespace FreakyNikki.ViewModels;

/// <summary>
/// Ties the UI to the audio engine, device monitor and settings. Owns the device
/// rows, the Start/Stop command, and all the marshalling of background device /
/// channel events back onto the UI thread.
/// </summary>
public sealed class MainViewModel : ViewModelBase, IDisposable
{
    private const int DefaultBluetoothDelayMs = 150;

    private readonly AudioEngine _engine;
    private readonly DeviceMonitor _monitor;
    private readonly SettingsStore _store;
    private readonly AppSettings _settings;
    private readonly UpdateService? _updates;
    private readonly Dispatcher _dispatcher;
    private readonly DispatcherTimer _saveTimer;

    private bool _isRunning;
    private bool _updateAvailable;
    private string _updateText = string.Empty;
    private bool _disposed;

    public MainViewModel(AudioEngine engine, DeviceMonitor monitor, SettingsStore store,
        AppSettings settings, UpdateService? updates = null)
    {
        _engine = engine;
        _monitor = monitor;
        _store = store;
        _settings = settings;
        _updates = updates;
        _dispatcher = Dispatcher.CurrentDispatcher;

        StartStopCommand = new RelayCommand(ToggleRun);
        UpdateCommand = new RelayCommand(() => _updates?.ApplyAndRestart(), () => UpdateAvailable);

        if (_updates is not null)
        {
            _updates.UpdateReady += version => _dispatcher.BeginInvoke(() =>
            {
                UpdateText = $"Update to v{version} — click to restart";
                UpdateAvailable = true;
            });
        }

        _saveTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(600) };
        _saveTimer.Tick += (_, _) => { _saveTimer.Stop(); _store.Save(_settings); };

        _monitor.DevicesChanged += () => _dispatcher.BeginInvoke(OnDevicesChanged);
        _monitor.DefaultChanged += id => _dispatcher.BeginInvoke(() => OnDefaultChanged(id));
        _engine.ChannelStatusChanged += (id, state, msg) =>
            _dispatcher.BeginInvoke(() => OnChannelStatus(id, state));

        RefreshDevices();
    }

    public ObservableCollection<DeviceRowViewModel> Devices { get; } = new();

    public RelayCommand StartStopCommand { get; }

    public RelayCommand UpdateCommand { get; }

    public bool UpdateAvailable
    {
        get => _updateAvailable;
        private set
        {
            if (SetProperty(ref _updateAvailable, value))
            {
                UpdateCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public string UpdateText
    {
        get => _updateText;
        private set => SetProperty(ref _updateText, value);
    }

    /// <summary>Kick off a background update check (no-op in dev builds).</summary>
    public void StartUpdateCheck() => _ = _updates?.CheckAsync();

    public string StartStopText => IsRunning ? "Stop" : "Start";

    public string StatusSummary
    {
        get
        {
            if (!IsRunning)
            {
                return "Stopped";
            }
            int count = Devices.Count(d => d.Enabled && d.IsControllable);
            return count == 0
                ? "Running — no extra devices selected"
                : $"Mirroring to {count} extra device{(count == 1 ? string.Empty : "s")}";
        }
    }

    public bool IsRunning
    {
        get => _isRunning;
        private set
        {
            if (SetProperty(ref _isRunning, value))
            {
                OnPropertyChanged(nameof(StartStopText));
                OnPropertyChanged(nameof(StatusSummary));
            }
        }
    }

    /// <summary>Start/stop; also invoked by the tray menu.</summary>
    public void ToggleRun()
    {
        if (IsRunning)
        {
            _engine.Stop();
            IsRunning = false;
            foreach (DeviceRowViewModel row in Devices.Where(d => d.IsControllable))
            {
                row.SetSilently(r => r.State = ChannelState.Idle);
            }
        }
        else
        {
            foreach (DeviceRowViewModel row in Devices.Where(d => d.Enabled && d.IsControllable))
            {
                _engine.EnableOutput(row.Id, (float)row.Volume, row.DelayMs);
            }
            _engine.Start();
            IsRunning = _engine.IsRunning; // false if capture couldn't start
        }

        OnPropertyChanged(nameof(StatusSummary));
    }

    private void RefreshDevices()
    {
        IReadOnlyList<DeviceInfo> snapshot = _monitor.GetRenderDevices();
        Dictionary<string, DeviceRowViewModel> previous = Devices.ToDictionary(d => d.Id);

        foreach (DeviceRowViewModel row in Devices)
        {
            DetachRow(row);
        }
        Devices.Clear();

        foreach (DeviceInfo info in snapshot)
        {
            var row = new DeviceRowViewModel(info.Id, info.Name, info.IsDefault);

            if (previous.TryGetValue(info.Id, out DeviceRowViewModel? old))
            {
                row.SetSilently(r =>
                {
                    r.Enabled = old.Enabled;
                    r.Volume = old.Volume;
                    r.DelayMs = old.DelayMs;
                    r.State = old.State;
                });
            }
            else if (_settings.Devices.TryGetValue(info.Id, out DeviceSetting? saved))
            {
                row.SetSilently(r =>
                {
                    r.Enabled = saved.Enabled;
                    r.Volume = saved.Volume;
                    r.DelayMs = saved.DelayMs;
                });
            }
            else if (!info.IsDefault)
            {
                // First time we've seen this device: pre-fill a sensible delay.
                // Bluetooth lags the wired default by ~100–250 ms; wired needs none.
                row.SetSilently(r => r.DelayMs = info.IsBluetooth ? DefaultBluetoothDelayMs : 0);
            }

            if (info.IsDefault)
            {
                // The default device plays natively; show it as active and locked.
                row.SetSilently(r => { r.Enabled = true; r.State = ChannelState.Playing; });
            }

            AttachRow(row);
            Devices.Add(row);

            // Keep the engine's config in sync; when running this also reconnects
            // a device that just came back.
            if (row.Enabled && row.IsControllable)
            {
                _engine.EnableOutput(row.Id, (float)row.Volume, row.DelayMs);
            }
        }

        OnPropertyChanged(nameof(StatusSummary));
    }

    private void OnDevicesChanged() => RefreshDevices();

    private void OnDefaultChanged(string _)
    {
        RefreshDevices();
        if (IsRunning)
        {
            _engine.RestartCapture();
        }
    }

    private void OnChannelStatus(string deviceId, ChannelState state)
    {
        DeviceRowViewModel? row = Devices.FirstOrDefault(d => d.Id == deviceId);
        row?.SetSilently(r => r.State = state);
    }

    private void AttachRow(DeviceRowViewModel row)
    {
        row.EnabledChanged += OnRowEnabledChanged;
        row.VolumeChanged += OnRowVolumeChanged;
        row.DelayChanged += OnRowDelayChanged;
    }

    private void DetachRow(DeviceRowViewModel row)
    {
        row.EnabledChanged -= OnRowEnabledChanged;
        row.VolumeChanged -= OnRowVolumeChanged;
        row.DelayChanged -= OnRowDelayChanged;
    }

    private void OnRowEnabledChanged(DeviceRowViewModel row)
    {
        if (!row.IsControllable)
        {
            return;
        }

        if (row.Enabled)
        {
            _engine.EnableOutput(row.Id, (float)row.Volume, row.DelayMs);
        }
        else
        {
            _engine.DisableOutput(row.Id);
            row.SetSilently(r => r.State = ChannelState.Idle);
        }

        Persist(row);
        OnPropertyChanged(nameof(StatusSummary));
    }

    private void OnRowVolumeChanged(DeviceRowViewModel row)
    {
        _engine.SetVolume(row.Id, (float)row.Volume);
        Persist(row);
    }

    private void OnRowDelayChanged(DeviceRowViewModel row)
    {
        _engine.SetDelay(row.Id, row.DelayMs);
        Persist(row);
    }

    private void Persist(DeviceRowViewModel row)
    {
        if (!row.IsControllable)
        {
            return;
        }

        _settings.Devices[row.Id] = new DeviceSetting
        {
            Enabled = row.Enabled,
            Volume = (float)row.Volume,
            DelayMs = row.DelayMs,
            Name = row.Name,
        };

        _saveTimer.Stop();
        _saveTimer.Start();
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }
        _disposed = true;

        _saveTimer.Stop();
        _store.Save(_settings);
    }
}
