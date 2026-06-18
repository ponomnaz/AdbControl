using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.IO;
using AdbControl.Application.Common;
using AdbControl.Application.Devices;
using AdbControl.Application.Remote;
using AdbControl.Core.Devices;

namespace AdbControl.Tools.Remote.ViewModels;

public sealed class RemoteViewModel : ObservableObject, IDisposable
{
    private readonly DeviceInventoryState _deviceInventory;
    private readonly DeviceAliasCatalog _deviceAliases;
    private readonly IRemoteControlService _remoteControl;
    private readonly bool _isScrcpyAvailable;
    private RemoteDeviceOptionViewModel? _selectedDevice;
    private bool _isMirroring;
    private bool _isConnecting;
    private bool _isDisposed;

    public RemoteViewModel(
        DeviceInventoryState deviceInventory,
        DeviceAliasCatalog deviceAliases,
        IRemoteControlService remoteControl)
    {
        _deviceInventory = deviceInventory;
        _deviceAliases = deviceAliases;
        _remoteControl = remoteControl;
        _isScrcpyAvailable = IsScrcpyInPath();

        SendKeyCommand = new RelayCommand<int>(
            keyCode => _ = SendKeyAsync(keyCode),
            _ => CanSendKey());

        StartMirroringCommand = new RelayCommand(
            () => IsMirroring = true,
            () => !_isDisposed && !_isMirroring && _isScrcpyAvailable && SelectedDevice is not null);

        StopMirroringCommand = new RelayCommand(
            () => IsMirroring = false,
            () => !_isDisposed && _isMirroring);

        _deviceInventory.KnownDevices.CollectionChanged += OnKnownDevicesChanged;
        _deviceAliases.Changed += OnAliasesChanged;

        RefreshConnectedDevices();
    }

    public ObservableCollection<RemoteDeviceOptionViewModel> ConnectedDevices { get; } = [];

    public RelayCommand<int> SendKeyCommand { get; }

    public RelayCommand StartMirroringCommand { get; }

    public RelayCommand StopMirroringCommand { get; }

    public RemoteDeviceOptionViewModel? SelectedDevice
    {
        get => _selectedDevice;
        set
        {
            if (!SetProperty(ref _selectedDevice, value))
            {
                return;
            }

            IsMirroring = false;
            OnPropertyChanged(nameof(TargetId));
            OnPropertyChanged(nameof(EmptyStateMessage));
            NotifyCommandStateChanged();
        }
    }

    public bool IsMirroring
    {
        get => _isMirroring;
        private set
        {
            if (!SetProperty(ref _isMirroring, value))
            {
                return;
            }

            _isConnecting = value;
            OnPropertyChanged(nameof(StatusText));
            NotifyCommandStateChanged();

            if (value && TargetId is not null)
            {
                _ = _remoteControl.StartSessionAsync(TargetId);
            }
            else
            {
                _remoteControl.StopSession();
            }
        }
    }

    public bool IsConnecting => _isConnecting;

    /// <summary>Called by the View when the scrcpy window has been embedded.</summary>
    public void NotifyWindowAttached()
    {
        _isConnecting = false;
        OnPropertyChanged(nameof(IsConnecting));
        OnPropertyChanged(nameof(StatusText));
    }

    public string? TargetId => _selectedDevice?.TargetId;

    public string DeviceSummary =>
        ConnectedDevices.Count == 0
            ? "Нет подключённых устройств"
            : ConnectedDevices.Count == 1
                ? "1 устройство"
                : $"Устройств: {ConnectedDevices.Count}";

    public string StatusText =>
        !_isScrcpyAvailable
            ? "scrcpy не найден в PATH — трансляция недоступна"
            : _isConnecting
                ? "Подключение…"
                : _isMirroring
                    ? "Трансляция активна"
                    : string.Empty;

    public string EmptyStateMessage =>
        !_isScrcpyAvailable
            ? "Установи scrcpy и добавь в PATH, чтобы видеть экран телевизора."
            : ConnectedDevices.Count == 0
                ? "Нет подключённых устройств."
                : SelectedDevice is null
                    ? "Выбери телевизор."
                    : "Нажми «Трансляция», чтобы увидеть экран.";

    private async Task SendKeyAsync(int keyCode)
    {
        if (SelectedDevice is null || _isDisposed)
        {
            return;
        }

        await _remoteControl.SendKeyEventAsync(SelectedDevice.TargetId, keyCode);
    }

    private bool CanSendKey()
    {
        return !_isDisposed && SelectedDevice is not null;
    }

    private void NotifyCommandStateChanged()
    {
        SendKeyCommand.NotifyCanExecuteChanged();
        StartMirroringCommand.NotifyCanExecuteChanged();
        StopMirroringCommand.NotifyCanExecuteChanged();
    }

    private void RefreshConnectedDevices()
    {
        var selectedTargetId = SelectedDevice?.TargetId;

        var options = _deviceInventory.KnownDevices
            .Where(static device => device.Reachability == DeviceReachability.Connected)
            .Select(BuildDeviceOption)
            .OrderBy(static option => option.DisplayText, StringComparer.CurrentCultureIgnoreCase)
            .ToArray();

        ConnectedDevices.Clear();
        foreach (var option in options)
        {
            ConnectedDevices.Add(option);
        }

        var nextSelected = options.FirstOrDefault(option =>
            string.Equals(option.TargetId, selectedTargetId, StringComparison.OrdinalIgnoreCase));

        if (!ReferenceEquals(SelectedDevice, nextSelected))
        {
            SelectedDevice = nextSelected;
        }

        OnPropertyChanged(nameof(DeviceSummary));
        OnPropertyChanged(nameof(EmptyStateMessage));
    }

    private RemoteDeviceOptionViewModel BuildDeviceOption(TvDeviceProfile device)
    {
        var targetId = string.IsNullOrWhiteSpace(device.NetworkEndpoint) ? device.Id : device.NetworkEndpoint;
        var alias = _deviceAliases.GetAlias(targetId);
        var displayText = string.IsNullOrWhiteSpace(alias) ? device.DisplayName : alias;
        return new RemoteDeviceOptionViewModel(targetId, displayText);
    }

    private static bool IsScrcpyInPath()
    {
        var paths = new[]
        {
            Environment.GetEnvironmentVariable("PATH", EnvironmentVariableTarget.Machine),
            Environment.GetEnvironmentVariable("PATH", EnvironmentVariableTarget.User),
            Environment.GetEnvironmentVariable("PATH", EnvironmentVariableTarget.Process)
        };

        return paths
            .Where(p => !string.IsNullOrEmpty(p))
            .SelectMany(p => p!.Split(';', StringSplitOptions.RemoveEmptyEntries))
            .Select(dir => dir.Trim())
            .Where(dir => !string.IsNullOrEmpty(dir))
            .Any(dir =>
            {
                try { return File.Exists(Path.Combine(dir, "scrcpy.exe")); }
                catch { return false; }
            });
    }

    private void OnKnownDevicesChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        RefreshConnectedDevices();
    }

    private void OnAliasesChanged(object? sender, EventArgs e)
    {
        RefreshConnectedDevices();
    }

    public void Dispose()
    {
        if (_isDisposed)
        {
            return;
        }

        _isDisposed = true;
        IsMirroring = false;
        _deviceInventory.KnownDevices.CollectionChanged -= OnKnownDevicesChanged;
        _deviceAliases.Changed -= OnAliasesChanged;
        NotifyCommandStateChanged();
    }
}

public sealed record RemoteDeviceOptionViewModel(string TargetId, string DisplayText)
{
    public override string ToString() => DisplayText;
}
