using System.Collections.ObjectModel;
using System.Collections.Specialized;
using AdbControl.Application.Common;
using AdbControl.Application.Devices;
using AdbControl.Core.Devices;

namespace AdbControl.Tools.Devices.ViewModels;

public sealed class DevicesToolViewModel : ObservableObject
{
    private readonly DeviceInventoryState _deviceInventory;
    private readonly IDeviceActionService _deviceActionService;
    private string _statusText = "Готово";

    public DevicesToolViewModel(DeviceInventoryState deviceInventory, IDeviceActionService deviceActionService)
    {
        _deviceInventory = deviceInventory;
        _deviceActionService = deviceActionService;

        PowerCommand = new RelayCommand(() => _ = TogglePowerAsync(), () => _deviceInventory.SelectedDevices.Count > 0);
        RebootCommand = new RelayCommand(() => _ = RebootAsync(), () => _deviceInventory.SelectedDevices.Count > 0);
        SelectedRows.CollectionChanged += OnSelectedRowsChanged;

        _deviceInventory.KnownDevices.CollectionChanged += OnDevicesChanged;
        _deviceInventory.SelectedDevices.CollectionChanged += OnSelectionChanged;

        RefreshKnownDevices();
    }

    public ObservableCollection<KnownDeviceRowViewModel> KnownDevices { get; } = [];

    public ObservableCollection<KnownDeviceRowViewModel> SelectedRows { get; } = [];

    public RelayCommand PowerCommand { get; }

    public RelayCommand RebootCommand { get; }

    public string StatusText
    {
        get => _statusText;
        private set => SetProperty(ref _statusText, value);
    }

    public string ConnectedSummary => $"Подключено: {_deviceInventory.KnownDevices.Count}";

    public string SelectionSummary => $"Выбрано: {_deviceInventory.SelectedDevices.Count}";

    public string EmptyStateMessage => _deviceInventory.KnownDevices.Count == 0
        ? "Нет подключенных устройств."
        : "Выбери одно или несколько устройств.";

    private async Task TogglePowerAsync()
    {
        StatusText = "Команда питания...";
        var result = await _deviceActionService.TogglePowerAsync(_deviceInventory.SelectedDevices.ToArray());
        StatusText = FormatResult("Питание", result);
    }

    private async Task RebootAsync()
    {
        StatusText = "Перезагрузка...";
        var result = await _deviceActionService.RebootAsync(_deviceInventory.SelectedDevices.ToArray());
        StatusText = FormatResult("Перезагрузка", result);
    }

    private void OnDevicesChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        RefreshKnownDevices();
        OnPropertyChanged(nameof(ConnectedSummary));
        OnPropertyChanged(nameof(EmptyStateMessage));
    }

    private void OnSelectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        OnPropertyChanged(nameof(SelectionSummary));
        OnPropertyChanged(nameof(EmptyStateMessage));
        PowerCommand.NotifyCanExecuteChanged();
        RebootCommand.NotifyCanExecuteChanged();
    }

    private void OnSelectedRowsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        _deviceInventory.ReplaceSelection(SelectedRows.Select(x => x.Device));
    }

    private static string FormatResult(string actionName, DeviceActionBatchResult result)
    {
        return result.FailureCount == 0
            ? $"{actionName}: {result.SuccessCount}"
            : $"{actionName}: {result.SuccessCount}, ошибок: {result.FailureCount}";
    }

    private void RefreshKnownDevices()
    {
        KnownDevices.Clear();
        foreach (var device in _deviceInventory.KnownDevices)
        {
            KnownDevices.Add(new KnownDeviceRowViewModel(
                device,
                device.DisplayName,
                device.NetworkEndpoint ?? "USB",
                GetConnectionLabel(device.PreferredConnection),
                GetReachabilityLabel(device.Reachability)));
        }

        SelectedRows.Clear();
    }

    private static string GetConnectionLabel(DeviceConnectionKind connectionKind) =>
        connectionKind switch
        {
            DeviceConnectionKind.Network => "Сеть",
            DeviceConnectionKind.Usb => "USB",
            _ => "Неизвестно"
        };

    private static string GetReachabilityLabel(DeviceReachability reachability) =>
        reachability switch
        {
            DeviceReachability.Connected => "Подключено",
            DeviceReachability.Reachable => "Доступно",
            DeviceReachability.Unreachable => "Недоступно",
            _ => "Неизвестно"
        };
}

public sealed record KnownDeviceRowViewModel(
    TvDeviceProfile Device,
    string DisplayName,
    string Endpoint,
    string ConnectionKind,
    string Reachability);
