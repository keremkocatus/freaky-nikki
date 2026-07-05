using FreakyNikki.Audio;

namespace FreakyNikki.ViewModels;

/// <summary>
/// One device row in the list: an enable checkbox, volume + delay sliders and a
/// status dot. Default device rows are locked (they already play natively).
/// User-driven changes raise the corresponding event; programmatic updates go
/// through <see cref="SetSilently"/> so they don't echo back to the engine.
/// </summary>
public sealed class DeviceRowViewModel : ViewModelBase
{
    private string _name;
    private bool _isDefault;
    private bool _enabled;
    private double _volume = 1.0;
    private int _delayMs;
    private ChannelState _state = ChannelState.Idle;
    private bool _suppress;

    public DeviceRowViewModel(string id, string name, bool isDefault)
    {
        Id = id;
        _name = name;
        _isDefault = isDefault;
    }

    public string Id { get; }

    public event Action<DeviceRowViewModel>? EnabledChanged;
    public event Action<DeviceRowViewModel>? VolumeChanged;
    public event Action<DeviceRowViewModel>? DelayChanged;

    public string Name
    {
        get => _name;
        set => SetProperty(ref _name, value);
    }

    public bool IsDefault
    {
        get => _isDefault;
        set
        {
            if (SetProperty(ref _isDefault, value))
            {
                OnPropertyChanged(nameof(IsControllable));
            }
        }
    }

    /// <summary>Default rows can't be toggled/adjusted — they play natively.</summary>
    public bool IsControllable => !_isDefault;

    public bool Enabled
    {
        get => _enabled;
        set
        {
            if (SetProperty(ref _enabled, value) && !_suppress)
            {
                EnabledChanged?.Invoke(this);
            }
        }
    }

    public double Volume
    {
        get => _volume;
        set
        {
            if (SetProperty(ref _volume, value) && !_suppress)
            {
                VolumeChanged?.Invoke(this);
            }
        }
    }

    public int DelayMs
    {
        get => _delayMs;
        set
        {
            int clamped = Math.Clamp(value, 0, 500);
            if (SetProperty(ref _delayMs, clamped) && !_suppress)
            {
                DelayChanged?.Invoke(this);
            }
        }
    }

    public ChannelState State
    {
        get => _state;
        set => SetProperty(ref _state, value);
    }

    /// <summary>Apply changes without raising user-change events.</summary>
    public void SetSilently(Action<DeviceRowViewModel> apply)
    {
        _suppress = true;
        try
        {
            apply(this);
        }
        finally
        {
            _suppress = false;
        }
    }
}
