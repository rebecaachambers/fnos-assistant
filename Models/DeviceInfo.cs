using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace FnosAssistant.Models;

public class DeviceInfo : INotifyPropertyChanged
{
    private string _ipAddress = string.Empty;
    private int _port;
    private string _hostname = string.Empty;
    private string _deviceName = string.Empty;
    private string _fnosVersion = string.Empty;
    private string _discoverMethod = string.Empty;
    private bool _isFnos;

    public string IpAddress
    {
        get => _ipAddress;
        set { _ipAddress = value; OnPropertyChanged(); }
    }

    public int Port
    {
        get => _port;
        set { _port = value; OnPropertyChanged(); }
    }

    public string Hostname
    {
        get => _hostname;
        set { _hostname = value; OnPropertyChanged(); }
    }

    public string DeviceName
    {
        get => _deviceName;
        set { _deviceName = value; OnPropertyChanged(); OnPropertyChanged(nameof(DisplayText)); }
    }

    public string FnosVersion
    {
        get => _fnosVersion;
        set { _fnosVersion = value; OnPropertyChanged(); OnPropertyChanged(nameof(VersionDisplay)); }
    }

    public string DiscoverMethod
    {
        get => _discoverMethod;
        set { _discoverMethod = value; OnPropertyChanged(); }
    }

    public bool IsFnos
    {
        get => _isFnos;
        set { _isFnos = value; OnPropertyChanged(); }
    }

    public string WebUrl => $"http://{IpAddress}";

    public string DisplayText => string.IsNullOrEmpty(DeviceName)
        ? $"{IpAddress}:{Port}"
        : $"{DeviceName} ({IpAddress}:{Port})";

    public string VersionDisplay => string.IsNullOrEmpty(FnosVersion) ? "检测中..." : FnosVersion;

    public event PropertyChangedEventHandler? PropertyChanged;
    protected void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
