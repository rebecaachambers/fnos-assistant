using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using FnosAssistant.Models;
using FnosAssistant.Services;

namespace FnosAssistant.ViewModels;

public class MainViewModel : INotifyPropertyChanged
{
    private readonly NetworkDiscoveryService _discovery = new();
    private bool _isScanning;
    private string _statusText = "就绪";

    public ObservableCollection<DeviceInfo> Devices { get; } = [];

    public bool IsScanning
    {
        get => _isScanning;
        set { _isScanning = value; OnPropertyChanged(); }
    }

    public string StatusText
    {
        get => _statusText;
        set { _statusText = value; OnPropertyChanged(); }
    }

    public ICommand ScanCommand { get; }

    public MainViewModel()
    {
        ScanCommand = new RelayCommand(async () => await ScanAsync(), () => !IsScanning);

        _discovery.DeviceDiscovered += device =>
        {
            System.Windows.Application.Current.Dispatcher.Invoke(() =>
            {
                Devices.Add(device);
                StatusText = $"已发现 {Devices.Count} 台设备，继续搜索中...";
            });
        };

        _discovery.DiscoveryCompleted += () =>
        {
            System.Windows.Application.Current.Dispatcher.Invoke(() =>
            {
                IsScanning = false;
                StatusText = Devices.Count > 0
                    ? $"扫描完成，找到 {Devices.Count} 台设备"
                    : "未发现设备，请检查网络";
            });
        };
    }

    private async Task ScanAsync()
    {
        Devices.Clear();
        IsScanning = true;
        StatusText = "正在扫描局域网...";
        await _discovery.StartDiscoveryAsync();
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    protected void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

public class RelayCommand : ICommand
{
    private readonly Action _execute;
    private readonly Func<bool> _canExecute;

    public RelayCommand(Action execute, Func<bool> canExecute)
    {
        _execute = execute;
        _canExecute = canExecute;
    }

    public bool CanExecute(object? parameter) => _canExecute();
    public void Execute(object? parameter) => _execute();
    public event EventHandler? CanExecuteChanged
    {
        add => CommandManager.RequerySuggested += value;
        remove => CommandManager.RequerySuggested -= value;
    }
}
