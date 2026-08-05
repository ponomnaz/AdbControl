using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Diagnostics;
using AdbControl.Application.Common;
using AdbControl.Application.Devices;
using AdbControl.Core.Devices;

namespace AdbControl.Tools.Devices.ViewModels;

public sealed class DevicesToolViewModel : ObservableObject
{
    private readonly DeviceInventoryState _deviceInventory;
    private readonly IDeviceActionService _deviceActionService;
    private readonly IDeviceScreenshotService _deviceScreenshotService;
    private readonly DeviceAliasCatalog _deviceAliases;
    private readonly NetariumServerEndpointCatalog _netariumServerEndpoint;
    private bool _isRefreshingSelection;
    private ServerEndpointOptionViewModel? _selectedServer;
    private string _serverEndpoint;
    private string _statusText = "Готово";

    public DevicesToolViewModel(
        DeviceInventoryState deviceInventory,
        IDeviceActionService deviceActionService,
        IDeviceScreenshotService deviceScreenshotService,
        DeviceAliasCatalog deviceAliases,
        NetariumServerEndpointCatalog netariumServerEndpoint)
    {
        _deviceInventory = deviceInventory;
        _deviceActionService = deviceActionService;
        _deviceScreenshotService = deviceScreenshotService;
        _deviceAliases = deviceAliases;
        _netariumServerEndpoint = netariumServerEndpoint;
        _serverEndpoint = netariumServerEndpoint.Endpoint;

        PowerCommand = new RelayCommand(() => _ = TogglePowerAsync(), () => _deviceInventory.SelectedDevices.Count > 0);
        RebootCommand = new RelayCommand(() => _ = RebootAsync(), () => _deviceInventory.SelectedDevices.Count > 0);
        StopCommand = new RelayCommand(() => _ = ForceStopAsync(), () => _deviceInventory.SelectedDevices.Count > 0);
        ClearCacheCommand = new RelayCommand(() => _ = ClearCacheAsync(), () => _deviceInventory.SelectedDevices.Count > 0);
        ScreenshotCommand = new RelayCommand(() => _ = ScreenshotAsync(), () => _deviceInventory.SelectedDevices.Count > 0);
        ApplyServerCommand = new RelayCommand(() => _ = ApplyServerAsync(), CanApplyServer);
        AddServerCommand = new RelayCommand(() => _ = AddServerAsync(), CanAddServer);
        RemoveServerCommand = new RelayCommand(() => _ = RemoveServerAsync(), CanRemoveServer);
        BeginEditAliasCommand = new RelayCommand<KnownDeviceRowViewModel>(BeginEditAlias);
        SaveAliasCommand = new RelayCommand<KnownDeviceRowViewModel>(row => _ = SaveAliasAsync(row));
        CancelAliasCommand = new RelayCommand<KnownDeviceRowViewModel>(CancelAliasEdit);
        SelectedRows.CollectionChanged += OnSelectedRowsChanged;

        _deviceInventory.KnownDevices.CollectionChanged += OnDevicesChanged;
        _deviceInventory.SelectedDevices.CollectionChanged += OnSelectionChanged;
        _deviceAliases.Changed += OnAliasesChanged;
        _netariumServerEndpoint.Changed += OnNetariumServerEndpointChanged;

        RefreshServers();
        RefreshKnownDevices();
    }

    public ObservableCollection<KnownDeviceRowViewModel> KnownDevices { get; } = [];

    public ObservableCollection<KnownDeviceRowViewModel> SelectedRows { get; } = [];

    public RelayCommand PowerCommand { get; }

    public RelayCommand RebootCommand { get; }

    public RelayCommand StopCommand { get; }

    public RelayCommand ClearCacheCommand { get; }

    public RelayCommand ScreenshotCommand { get; }

    public RelayCommand ApplyServerCommand { get; }

    public RelayCommand AddServerCommand { get; }

    public RelayCommand RemoveServerCommand { get; }

    public ObservableCollection<ServerEndpointOptionViewModel> Servers { get; } = [];

    /// <summary>
    /// Выбор в списке подставляется в поле адреса. Обратной связи нет намеренно:
    /// в поле можно набрать адрес, которого в списке ещё нет.
    /// </summary>
    public ServerEndpointOptionViewModel? SelectedServer
    {
        get => _selectedServer;
        set
        {
            if (SetProperty(ref _selectedServer, value) && value is not null)
            {
                ServerEndpoint = value.Endpoint;
            }
        }
    }

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
            AddServerCommand.NotifyCanExecuteChanged();
            RemoveServerCommand.NotifyCanExecuteChanged();
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

    private async Task ScreenshotAsync()
    {
        StatusText = "Снимок экрана...";

        var result = await _deviceScreenshotService.CaptureAsync(_deviceInventory.SelectedDevices.ToArray());

        StatusText = result.FailureCount == 0
            ? $"Снимков: {result.SuccessCount}"
            : $"Снимков: {result.SuccessCount}, ошибок: {result.FailureCount}";

        OpenFiles(result.FilePaths);
    }

    private void OpenFiles(IReadOnlyList<string> filePaths)
    {
        foreach (var filePath in filePaths)
        {
            try
            {
                Process.Start(new ProcessStartInfo(filePath) { UseShellExecute = true });
            }
            catch (Exception ex)
            {
                // Снимок уже сохранён — сообщаем, но результат не теряем.
                StatusText = $"Файл сохранён, но не открылся: {ex.Message}";
            }
        }
    }

    private async Task AddServerAsync()
    {
        try
        {
            await _netariumServerEndpoint.AddEndpointAsync(ServerEndpoint);
            StatusText = $"Сервер добавлен: {ServerEndpoint}";
        }
        catch (Exception ex)
        {
            StatusText = $"Ошибка: {ex.Message}";
        }
    }

    private async Task RemoveServerAsync()
    {
        var removedEndpoint = ServerEndpoint;

        try
        {
            await _netariumServerEndpoint.RemoveEndpointAsync(removedEndpoint);
            StatusText = $"Сервер убран из списка: {removedEndpoint}";
        }
        catch (Exception ex)
        {
            StatusText = $"Ошибка: {ex.Message}";
        }
    }

    private bool CanAddServer()
    {
        return !string.IsNullOrWhiteSpace(ServerEndpoint) &&
               !_netariumServerEndpoint.GetEndpoints().Contains(ServerEndpoint, StringComparer.OrdinalIgnoreCase);
    }

    private bool CanRemoveServer()
    {
        return !string.IsNullOrWhiteSpace(ServerEndpoint) &&
               _netariumServerEndpoint.GetEndpoints().Contains(ServerEndpoint, StringComparer.OrdinalIgnoreCase);
    }

    private void RefreshServers()
    {
        Servers.Clear();
        foreach (var endpoint in _netariumServerEndpoint.GetEndpoints())
        {
            Servers.Add(new ServerEndpointOptionViewModel(endpoint, endpoint));
        }

        // Выделение выставляем без обратной записи: иначе обновление списка
        // затирало бы адрес, набранный в поле руками.
        _selectedServer = Servers.FirstOrDefault(option =>
            string.Equals(option.Endpoint, ServerEndpoint, StringComparison.OrdinalIgnoreCase));

        OnPropertyChanged(nameof(SelectedServer));
        AddServerCommand.NotifyCanExecuteChanged();
        RemoveServerCommand.NotifyCanExecuteChanged();
    }

    private async Task ClearCacheAsync()
    {
        StatusText = "Очистка кэша...";

        var result = await _deviceActionService.ClearNetariumCacheAsync(_deviceInventory.SelectedDevices.ToArray());

        var summary = $"Кэш очищен: {result.SuccessCount}, освобождено {FormatSize(result.FreedBytes)}";

        if (result.FailureCount > 0)
        {
            summary += $", ошибок: {result.FailureCount}";
        }

        // Про незапустившееся приложение молчать нельзя: телевизор останется с пустым экраном.
        if (result.RelaunchedCount < result.SuccessCount)
        {
            summary += $", не запустилось: {result.SuccessCount - result.RelaunchedCount}";
        }

        StatusText = summary;
    }

    private static string FormatSize(long bytes)
    {
        if (bytes >= 1024L * 1024 * 1024)
        {
            return $"{bytes / (1024d * 1024 * 1024):0.#} ГБ";
        }

        if (bytes >= 1024 * 1024)
        {
            return $"{bytes / (1024d * 1024):0.#} МБ";
        }

        return bytes >= 1024
            ? $"{bytes / 1024d:0.#} КБ"
            : $"{bytes} Б";
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
        NotifySelectionCommandsChanged();
    }

    /// <summary>
    /// Все команды, зависящие от выделения, — одним списком. RelayCommand не слушает
    /// CommandManager и пересчитывает CanExecute только по явному уведомлению, поэтому
    /// забытая здесь команда навсегда застревает в состоянии на момент привязки.
    /// </summary>
    private void NotifySelectionCommandsChanged()
    {
        PowerCommand.NotifyCanExecuteChanged();
        RebootCommand.NotifyCanExecuteChanged();
        StopCommand.NotifyCanExecuteChanged();
        ClearCacheCommand.NotifyCanExecuteChanged();
        ScreenshotCommand.NotifyCanExecuteChanged();
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
            var index = 0;

            foreach (var device in _deviceInventory.KnownDevices)
            {
                var key = GetDeviceKey(device);
                var endpoint = device.NetworkEndpoint ?? AdbTransportName.Describe(device.Id);
                var connection = GetConnectionLabel(device.PreferredConnection);
                var reachability = GetReachabilityLabel(device.Reachability);

                if (FindRow(key) is { } existing)
                {
                    existing.Update(device, endpoint, connection, reachability);
                    existing.ApplyAlias(_deviceAliases.GetAlias(key));

                    var current = KnownDevices.IndexOf(existing);
                    if (current != index)
                    {
                        KnownDevices.Move(current, index);
                    }
                }
                else
                {
                    var row = new KnownDeviceRowViewModel(device, key, endpoint, connection, reachability);
                    row.ApplyAlias(_deviceAliases.GetAlias(key));
                    KnownDevices.Insert(index, row);
                }

                index++;
            }

            while (KnownDevices.Count > index)
            {
                KnownDevices.RemoveAt(KnownDevices.Count - 1);
            }

            SyncSelectedRows(selectedKeys);
        }
        finally
        {
            _isRefreshingSelection = false;
        }
    }

    private KnownDeviceRowViewModel? FindRow(string aliasKey)
    {
        foreach (var row in KnownDevices)
        {
            if (string.Equals(row.AliasKey, aliasKey, StringComparison.OrdinalIgnoreCase))
            {
                return row;
            }
        }

        return null;
    }

    /// <summary>
    /// Выделение правится точечно теми же объектами строк: пересобранное целиком, оно
    /// сбрасывалось при каждом обновлении списка устройств.
    /// </summary>
    private void SyncSelectedRows(IReadOnlySet<string> selectedKeys)
    {
        for (var index = SelectedRows.Count - 1; index >= 0; index--)
        {
            var row = SelectedRows[index];

            if (!selectedKeys.Contains(row.AliasKey) || !KnownDevices.Contains(row))
            {
                SelectedRows.RemoveAt(index);
            }
        }

        foreach (var row in KnownDevices)
        {
            if (selectedKeys.Contains(row.AliasKey) && !SelectedRows.Contains(row))
            {
                SelectedRows.Add(row);
            }
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
        if (!string.Equals(ServerEndpoint, endpoint, StringComparison.Ordinal))
        {
            _serverEndpoint = endpoint;
            OnPropertyChanged(nameof(ServerEndpoint));
            ApplyServerCommand.NotifyCanExecuteChanged();
        }

        RefreshServers();
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

/// <summary>
/// Элемент выпадающего списка серверов. <see cref="DisplayText"/> — общая для приложения
/// конвенция отображения в <c>ComboBoxInputStyle</c>.
/// </summary>
public sealed record ServerEndpointOptionViewModel(string Endpoint, string DisplayText)
{
    public override string ToString() => DisplayText;
}

public sealed class KnownDeviceRowViewModel : ObservableObject
{
    private string _title;
    private string _secondaryText = string.Empty;
    private string _aliasDraft = string.Empty;
    private bool _isEditingAlias;
    private bool _hasSecondaryText;
    private TvDeviceProfile _device;
    private string _endpoint;
    private string _connectionKind;
    private string _reachability;

    public KnownDeviceRowViewModel(
        TvDeviceProfile device,
        string aliasKey,
        string endpoint,
        string connectionKind,
        string reachability)
    {
        _device = device;
        AliasKey = aliasKey;
        _endpoint = endpoint;
        _connectionKind = connectionKind;
        _reachability = reachability;
        _title = endpoint;
    }

    public TvDeviceProfile Device
    {
        get => _device;
        private set => SetProperty(ref _device, value);
    }

    /// <summary>Опознание строки при сверке списка: не меняется, пока это то же устройство.</summary>
    public string AliasKey { get; }

    public string Endpoint
    {
        get => _endpoint;
        private set => SetProperty(ref _endpoint, value);
    }

    public string ConnectionKind
    {
        get => _connectionKind;
        private set => SetProperty(ref _connectionKind, value);
    }

    public string Reachability
    {
        get => _reachability;
        private set => SetProperty(ref _reachability, value);
    }

    /// <summary>
    /// Обновление на месте вместо пересоздания строки: пересозданная теряет выделение
    /// и открытое поле ввода имени.
    /// </summary>
    public void Update(TvDeviceProfile device, string endpoint, string connectionKind, string reachability)
    {
        Device = device;
        Endpoint = endpoint;
        ConnectionKind = connectionKind;
        Reachability = reachability;
    }

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
