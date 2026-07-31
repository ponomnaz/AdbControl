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
    private readonly DeviceAliasCatalog _deviceAliases;
    private readonly NetariumServerEndpointCatalog _netariumServerEndpoint;
    private bool _isRefreshingSelection;
    private string _serverEndpoint;
    private string _statusText = "Готово";

    public DevicesToolViewModel(
        DeviceInventoryState deviceInventory,
        IDeviceActionService deviceActionService,
        DeviceAliasCatalog deviceAliases,
        NetariumServerEndpointCatalog netariumServerEndpoint)
    {
        _deviceInventory = deviceInventory;
        _deviceActionService = deviceActionService;
        _deviceAliases = deviceAliases;
        _netariumServerEndpoint = netariumServerEndpoint;
        _serverEndpoint = netariumServerEndpoint.Endpoint;

        PowerCommand = new RelayCommand(() => _ = TogglePowerAsync(), () => _deviceInventory.SelectedDevices.Count > 0);
        RebootCommand = new RelayCommand(() => _ = RebootAsync(), () => _deviceInventory.SelectedDevices.Count > 0);
        StopCommand = new RelayCommand(() => _ = ForceStopAsync(), () => _deviceInventory.SelectedDevices.Count > 0);
        ApplyServerCommand = new RelayCommand(() => _ = ApplyServerAsync(), CanApplyServer);
        BeginEditAliasCommand = new RelayCommand<KnownDeviceRowViewModel>(BeginEditAlias);
        SaveAliasCommand = new RelayCommand<KnownDeviceRowViewModel>(row => _ = SaveAliasAsync(row));
        CancelAliasCommand = new RelayCommand<KnownDeviceRowViewModel>(CancelAliasEdit);
        SelectedRows.CollectionChanged += OnSelectedRowsChanged;

        _deviceInventory.KnownDevices.CollectionChanged += OnDevicesChanged;
        _deviceInventory.SelectedDevices.CollectionChanged += OnSelectionChanged;
        _deviceAliases.Changed += OnAliasesChanged;
        _netariumServerEndpoint.Changed += OnNetariumServerEndpointChanged;

        RefreshKnownDevices();
    }

    public ObservableCollection<KnownDeviceRowViewModel> KnownDevices { get; } = [];

    public ObservableCollection<KnownDeviceRowViewModel> SelectedRows { get; } = [];

    public RelayCommand PowerCommand { get; }

    public RelayCommand RebootCommand { get; }

    public RelayCommand StopCommand { get; }

    public RelayCommand ApplyServerCommand { get; }

    public RelayCommand<KnownDeviceRowViewModel> BeginEditAliasCommand { get; }

    public RelayCommand<KnownDeviceRowViewModel> SaveAliasCommand { get; }

    public RelayCommand<KnownDeviceRowViewModel> CancelAliasCommand { get; }

    public string StatusText
    {
        get => _statusText;
        private set => SetProperty(ref _statusText, value);
    }

    public string ServerEndpoint
    {
        get => _serverEndpoint;
        set
        {
            var normalizedValue = NetariumServerEndpointCatalog.NormalizeEndpoint(value) ?? string.Empty;
            if (!SetProperty(ref _serverEndpoint, normalizedValue))
            {
                return;
            }

            ApplyServerCommand.NotifyCanExecuteChanged();
            _ = PersistServerEndpointAsync(normalizedValue);
        }
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
        if (result.SuccessfulEndpoints.Count > 0)
        {
            _deviceInventory.RemoveKnownDevicesByEndpoint(result.SuccessfulEndpoints);
        }

        StatusText = FormatResult("Питание", result);
    }

    private async Task RebootAsync()
    {
        StatusText = "Перезагрузка...";
        var result = await _deviceActionService.RebootAsync(_deviceInventory.SelectedDevices.ToArray());
        if (result.SuccessfulEndpoints.Count > 0)
        {
            _deviceInventory.RemoveKnownDevicesByEndpoint(result.SuccessfulEndpoints);
        }

        StatusText = FormatResult("Перезагрузка", result);
    }

    private async Task ForceStopAsync()
    {
        StatusText = "Остановка...";
        var result = await _deviceActionService.ForceStopNetariumAsync(_deviceInventory.SelectedDevices.ToArray());
        StatusText = FormatResult("Стоп", result);
    }

    private async Task ApplyServerAsync()
    {
        if (!CanApplyServer())
        {
            return;
        }

        StatusText = "Сервер...";
        var result = await _deviceActionService.ConfigureNetariumServerAsync(_deviceInventory.SelectedDevices.ToArray(), ServerEndpoint);
        StatusText = FormatResult("Сервер", result);
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
        StopCommand.NotifyCanExecuteChanged();
        ApplyServerCommand.NotifyCanExecuteChanged();
    }

    private void OnSelectedRowsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (_isRefreshingSelection)
        {
            return;
        }

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
        var selectedKeys = _deviceInventory.SelectedDevices
            .Select(GetDeviceKey)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        _isRefreshingSelection = true;
        try
        {
            KnownDevices.Clear();
            SelectedRows.Clear();

            foreach (var device in _deviceInventory.KnownDevices)
            {
                var row = new KnownDeviceRowViewModel(
                    device,
                    GetDeviceKey(device),
                    device.NetworkEndpoint ?? AdbTransportName.Describe(device.Id),
                    GetConnectionLabel(device.PreferredConnection),
                    GetReachabilityLabel(device.Reachability));

                row.ApplyAlias(_deviceAliases.GetAlias(row.AliasKey));
                KnownDevices.Add(row);

                if (selectedKeys.Contains(row.AliasKey))
                {
                    SelectedRows.Add(row);
                }
            }
        }
        finally
        {
            _isRefreshingSelection = false;
        }
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

    private void OnAliasesChanged(object? sender, EventArgs e)
    {
        foreach (var row in KnownDevices)
        {
            row.ApplyAlias(_deviceAliases.GetAlias(row.AliasKey));
        }
    }

    private void OnNetariumServerEndpointChanged(object? sender, EventArgs e)
    {
        var endpoint = _netariumServerEndpoint.Endpoint;
        if (string.Equals(ServerEndpoint, endpoint, StringComparison.Ordinal))
        {
            return;
        }

        _serverEndpoint = endpoint;
        OnPropertyChanged(nameof(ServerEndpoint));
        ApplyServerCommand.NotifyCanExecuteChanged();
    }

    private void BeginEditAlias(KnownDeviceRowViewModel? row)
    {
        if (row is null)
        {
            return;
        }

        foreach (var deviceRow in KnownDevices)
        {
            if (!ReferenceEquals(deviceRow, row) && deviceRow.IsEditingAlias)
            {
                deviceRow.CancelAliasEdit();
            }
        }

        row.BeginAliasEdit();
    }

    private async Task SaveAliasAsync(KnownDeviceRowViewModel? row)
    {
        if (row is null)
        {
            return;
        }

        try
        {
            var alias = string.IsNullOrWhiteSpace(row.AliasDraft)
                ? null
                : row.AliasDraft.Trim();

            await _deviceAliases.SetAliasAsync(row.AliasKey, alias);
            UpdateInventoryDisplayName(row.AliasKey, alias);
            row.EndAliasEdit();
        }
        catch
        {
            row.CancelAliasEdit();
            StatusText = "Не удалось сохранить имя";
        }
    }

    private void CancelAliasEdit(KnownDeviceRowViewModel? row)
    {
        row?.CancelAliasEdit();
    }

    public void CancelActiveAliasEdit()
    {
        var editingRow = KnownDevices.FirstOrDefault(static row => row.IsEditingAlias);
        editingRow?.CancelAliasEdit();
    }

    private void UpdateInventoryDisplayName(string aliasKey, string? alias)
    {
        var updatedDevices = _deviceInventory.KnownDevices
            .Where(device => string.Equals(GetDeviceKey(device), aliasKey, StringComparison.OrdinalIgnoreCase))
            .Select(device => device with
            {
                DisplayName = string.IsNullOrWhiteSpace(alias)
                    ? device.NetworkEndpoint ?? device.Id
                    : alias
            })
            .ToArray();

        if (updatedDevices.Length > 0)
        {
            _deviceInventory.UpsertKnownDevices(updatedDevices);
        }
    }

    private static string GetDeviceKey(TvDeviceProfile device)
    {
        return string.IsNullOrWhiteSpace(device.NetworkEndpoint)
            ? device.Id
            : device.NetworkEndpoint;
    }

    private bool CanApplyServer()
    {
        return _deviceInventory.SelectedDevices.Count > 0 &&
               !string.IsNullOrWhiteSpace(ServerEndpoint);
    }

    private async Task PersistServerEndpointAsync(string endpoint)
    {
        try
        {
            await _netariumServerEndpoint.SetEndpointAsync(endpoint);
        }
        catch
        {
            // Keep editing responsive even if settings persistence fails.
        }
    }
}

public sealed class KnownDeviceRowViewModel : ObservableObject
{
    private string _title;
    private string _secondaryText = string.Empty;
    private string _aliasDraft = string.Empty;
    private bool _isEditingAlias;
    private bool _hasSecondaryText;

    public KnownDeviceRowViewModel(
        TvDeviceProfile device,
        string aliasKey,
        string endpoint,
        string connectionKind,
        string reachability)
    {
        Device = device;
        AliasKey = aliasKey;
        Endpoint = endpoint;
        ConnectionKind = connectionKind;
        Reachability = reachability;
        _title = endpoint;
    }

    public TvDeviceProfile Device { get; }

    public string AliasKey { get; }

    public string Endpoint { get; }

    public string ConnectionKind { get; }

    public string Reachability { get; }

    public string Title
    {
        get => _title;
        private set => SetProperty(ref _title, value);
    }

    public string SecondaryText
    {
        get => _secondaryText;
        private set => SetProperty(ref _secondaryText, value);
    }

    public bool HasSecondaryText
    {
        get => _hasSecondaryText;
        private set => SetProperty(ref _hasSecondaryText, value);
    }

    public string AliasDraft
    {
        get => _aliasDraft;
        set => SetProperty(ref _aliasDraft, value);
    }

    public bool IsEditingAlias
    {
        get => _isEditingAlias;
        private set => SetProperty(ref _isEditingAlias, value);
    }

    public void ApplyAlias(string? alias)
    {
        var normalizedAlias = string.IsNullOrWhiteSpace(alias)
            ? null
            : alias.Trim();

        Title = normalizedAlias ?? Endpoint;
        SecondaryText = normalizedAlias is null
            ? string.Empty
            : Endpoint;
        HasSecondaryText = normalizedAlias is not null;

        if (!IsEditingAlias)
        {
            AliasDraft = normalizedAlias ?? string.Empty;
        }
    }

    public void BeginAliasEdit()
    {
        AliasDraft = string.Equals(Title, Endpoint, StringComparison.Ordinal)
            ? string.Empty
            : Title;
        IsEditingAlias = true;
    }

    public void EndAliasEdit()
    {
        IsEditingAlias = false;
    }

    public void CancelAliasEdit()
    {
        AliasDraft = string.Equals(Title, Endpoint, StringComparison.Ordinal)
            ? string.Empty
            : Title;
        IsEditingAlias = false;
    }
}
