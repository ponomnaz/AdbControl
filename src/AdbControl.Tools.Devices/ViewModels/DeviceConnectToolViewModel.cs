using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Globalization;
using System.Windows.Data;
using AdbControl.Application.Common;
using AdbControl.Application.Devices;
using AdbControl.Core.Devices;

namespace AdbControl.Tools.Devices.ViewModels;

public sealed class DeviceConnectToolViewModel : ObservableObject
{
    private readonly DeviceInventoryState _deviceInventory;
    private readonly IDeviceDiscoveryService _deviceDiscoveryService;
    private readonly IAdbConnectionService _adbConnectionService;
    private readonly DeviceAliasCatalog _deviceAliases;
    private readonly AutoConnectDeviceCatalog _autoConnectDevices;
    private readonly Dictionary<string, string> _endpointModels = new(StringComparer.OrdinalIgnoreCase);
    private CancellationTokenSource? _scanCancellation;
    private ScanProgress? _scanProgress;
    private bool _isScanning;
    private bool _isConnecting;
    private bool _isDisconnecting;
    private bool _isTogglingAutoConnect;
    private string _statusText = string.Empty;
    private string _endpointInput = string.Empty;
    private string _scanTargetInput = string.Empty;
    private string _portsInput = string.Empty;
    private double _scanProgressFraction;

    public DeviceConnectToolViewModel(
        DeviceInventoryState deviceInventory,
        IDeviceDiscoveryService deviceDiscoveryService,
        IAdbConnectionService adbConnectionService,
        DeviceAliasCatalog deviceAliases,
        AutoConnectDeviceCatalog autoConnectDevices)
    {
        _deviceInventory = deviceInventory;
        _deviceDiscoveryService = deviceDiscoveryService;
        _adbConnectionService = adbConnectionService;
        _deviceAliases = deviceAliases;
        _autoConnectDevices = autoConnectDevices;

        DiscoveredDevicesView = CollectionViewSource.GetDefaultView(DiscoveredDevices);
        DiscoveredDevicesView.Filter = FilterDiscoveredDevice;

        RefreshCommand = new RelayCommand(
            () => _ = RefreshAsync(),
            () => !_isScanning && !_isConnecting && !_isDisconnecting && !_isTogglingAutoConnect);

        StopScanCommand = new RelayCommand(
            () => _scanCancellation?.Cancel(),
            () => _isScanning);

        ConnectSelectedCommand = new RelayCommand(
            () => _ = ConnectSelectedAsync(),
            () => CanConnectSelected());

        ConnectManualCommand = new RelayCommand(
            () => _ = ConnectManualAsync(),
            () => CanConnectManual());

        DisconnectSelectedCommand = new RelayCommand(
            () => _ = DisconnectSelectedAsync(),
            () => CanDisconnectSelected());

        ToggleAutoConnectCommand = new RelayCommand<string>(
            endpoint => _ = ToggleAutoConnectAsync(endpoint),
            endpoint => CanToggleAutoConnect(endpoint));

        BeginEditAliasCommand = new RelayCommand<DiscoveredDeviceItemViewModel>(BeginEditAlias);
        SaveAliasCommand = new RelayCommand<DiscoveredDeviceItemViewModel>(row => _ = SaveAliasAsync(row));
        CancelAliasCommand = new RelayCommand<DiscoveredDeviceItemViewModel>(CancelAliasEdit);

        DiscoveredDevices.CollectionChanged += OnDiscoveredDevicesChanged;
        SelectedCandidates.CollectionChanged += OnSelectedCandidatesChanged;
        _deviceInventory.KnownDevices.CollectionChanged += OnKnownDevicesChanged;
        _deviceAliases.Changed += OnAliasesChanged;
        _autoConnectDevices.Changed += OnAutoConnectCatalogChanged;
        _ = RefreshAsync();
    }

    public ObservableCollection<DiscoveredDeviceItemViewModel> DiscoveredDevices { get; } = [];

    public ObservableCollection<DiscoveredDeviceItemViewModel> SelectedCandidates { get; } = [];

    public ICollectionView DiscoveredDevicesView { get; }

    public RelayCommand RefreshCommand { get; }

    public RelayCommand StopScanCommand { get; }

    public RelayCommand ConnectSelectedCommand { get; }

    public RelayCommand ConnectManualCommand { get; }

    public RelayCommand DisconnectSelectedCommand { get; }

    public RelayCommand<string> ToggleAutoConnectCommand { get; }

    public RelayCommand<DiscoveredDeviceItemViewModel> BeginEditAliasCommand { get; }

    public RelayCommand<DiscoveredDeviceItemViewModel> SaveAliasCommand { get; }

    public RelayCommand<DiscoveredDeviceItemViewModel> CancelAliasCommand { get; }

    public string StatusText
    {
        get => _statusText;
        private set => SetProperty(ref _statusText, value);
    }

    public string EndpointInput
    {
        get => _endpointInput;
        set
        {
            if (!SetProperty(ref _endpointInput, value))
            {
                return;
            }

            DiscoveredDevicesView.Refresh();
            SelectedCandidates.Clear();
            OnPropertyChanged(nameof(DiscoverySummary));
            ConnectManualCommand.NotifyCanExecuteChanged();
        }
    }

    /// <summary>
    /// Цель сканирования: пусто — локальные подсети, иначе CIDR / диапазон / хост / хост:порт.
    /// </summary>
    public string ScanTargetInput
    {
        get => _scanTargetInput;
        set => SetProperty(ref _scanTargetInput, value);
    }

    /// <summary>Порты для проверки; пусто — 5555.</summary>
    public string PortsInput
    {
        get => _portsInput;
        set => SetProperty(ref _portsInput, value);
    }

    public bool IsScanning
    {
        get => _isScanning;
        private set
        {
            if (SetProperty(ref _isScanning, value))
            {
                OnPropertyChanged(nameof(DiscoverySummary));
            }
        }
    }

    public double ScanProgressFraction
    {
        get => _scanProgressFraction;
        private set => SetProperty(ref _scanProgressFraction, value);
    }

    public string DiscoverySummary
    {
        get
        {
            if (_isScanning)
            {
                return _scanProgress is { TotalProbes: > 0 } progress
                    ? $"Поиск... {progress.CompletedProbes}/{progress.TotalProbes}"
                    : "Поиск...";
            }

            var visibleCount = DiscoveredDevicesView.Cast<object>().Count();
            return string.IsNullOrWhiteSpace(EndpointInput)
                ? $"Найдено: {visibleCount}"
                : $"Показано: {visibleCount} из {DiscoveredDevices.Count}";
        }
    }

    public string SelectionSummary => $"Выбрано: {SelectedCandidates.Count}";

    private async Task RefreshAsync()
    {
        if (!TryBuildScanProfile(out var profile, out var profileError))
        {
            StatusText = profileError ?? "Неверная цель сканирования";
            return;
        }

        using var scanCancellation = new CancellationTokenSource();
        _scanCancellation = scanCancellation;

        try
        {
            IsScanning = true;
            _scanProgress = null;
            ScanProgressFraction = 0d;
            NotifyCommandStateChanged();

            SelectedCandidates.Clear();
            DiscoveredDevices.Clear();
            StatusText = "Поиск...";

            var seenEndpoints = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            // Уже подключённые устройства показываем независимо от того, попадают ли
            // они в зону сканирования: проброшенный endpoint иначе остаётся невидимым.
            foreach (var endpoint in await SyncConnectedDevicesAsync())
            {
                if (seenEndpoints.Add(endpoint))
                {
                    DiscoveredDevices.Add(CreateDiscoveredItem(endpoint, "Уже подключено"));
                }
            }

            var scanProgress = new Progress<ScanProgress>(OnScanProgress);

            await foreach (var discovered in _deviceDiscoveryService.DiscoverAsync(
                               profile,
                               scanProgress,
                               scanCancellation.Token))
            {
                if (seenEndpoints.Add(discovered.Endpoint))
                {
                    DiscoveredDevices.Add(CreateDiscoveredItem(
                        discovered.Endpoint,
                        IsConnected(discovered.Endpoint) ? "Уже подключено" : "Готово",
                        discovered.State,
                        discovered.Model));
                }
            }

            RefreshAutoConnectFlags();
            StatusText = string.Empty;
        }
        catch (OperationCanceledException)
        {
            RefreshAutoConnectFlags();
            StatusText = "Сканирование остановлено";
        }
        catch (ScanBudgetExceededException ex)
        {
            StatusText = ex.Message;
        }
        catch (Exception ex)
        {
            StatusText = $"Ошибка: {ex.Message}";
        }
        finally
        {
            _scanCancellation = null;
            _scanProgress = null;
            ScanProgressFraction = 0d;
            IsScanning = false;
            NotifyCommandStateChanged();
        }
    }

    private bool TryBuildScanProfile(out ScanProfile profile, out string? error)
    {
        profile = ScanProfile.LocalNetwork;

        if (!ScanTargetParser.TryParse(ScanTargetInput, out var targets, out error))
        {
            return false;
        }

        if (!PortSpec.TryParse(PortsInput, out var ports, out error))
        {
            return false;
        }

        profile = targets.Count == 0
            ? ScanProfile.LocalNetwork with { Ports = ports }
            : ScanProfile.ForTargets(targets, ports);

        return true;
    }

    private void OnScanProgress(ScanProgress progress)
    {
        _scanProgress = progress;
        ScanProgressFraction = progress.Fraction;
        OnPropertyChanged(nameof(DiscoverySummary));
    }

    private async Task ConnectSelectedAsync()
    {
        var selectedItems = SelectedCandidates.ToArray();
        if (selectedItems.Length == 0)
        {
            return;
        }

        try
        {
            _isConnecting = true;
            NotifyCommandStateChanged();
            StatusText = selectedItems.Length == 1 ? "Подключение..." : $"Подключение: {selectedItems.Length}";

            foreach (var item in selectedItems)
            {
                item.Status = IsConnected(item.Endpoint) ? "Уже подключено" : "Готово";
            }

            var connectedDevices = await ConnectEndpointsAsync(selectedItems
                .Select(x => new ConnectRequest(x.Endpoint, status => x.Status = status))
                .ToArray());

            await SyncConnectedDevicesAsync();

            var failedCount = selectedItems.Length - connectedDevices.Count;
            StatusText = failedCount == 0
                ? $"Подключено: {connectedDevices.Count}"
                : $"Подключено: {connectedDevices.Count}, ошибок: {failedCount}";
        }
        catch (Exception ex)
        {
            StatusText = $"Ошибка: {ex.Message}";
        }
        finally
        {
            _isConnecting = false;
            NotifyCommandStateChanged();
        }
    }

    private async Task DisconnectSelectedAsync()
    {
        var selectedItems = SelectedCandidates.ToArray();
        if (selectedItems.Length == 0)
        {
            return;
        }

        try
        {
            _isDisconnecting = true;
            NotifyCommandStateChanged();
            StatusText = selectedItems.Length == 1 ? "Отключение..." : $"Отключение: {selectedItems.Length}";

            var disconnectedEndpoints = new List<string>();
            var failedCount = 0;

            foreach (var item in selectedItems)
            {
                if (!IsConnected(item.Endpoint))
                {
                    item.Status = "Не подключено";
                    continue;
                }

                item.Status = "Отключение...";
                var result = await _adbConnectionService.DisconnectAsync(item.Endpoint);
                item.Status = result.IsSuccess ? "Готово" : result.Message;

                if (result.IsSuccess)
                {
                    disconnectedEndpoints.Add(item.Endpoint);
                }
                else
                {
                    failedCount++;
                }
            }

            if (disconnectedEndpoints.Count > 0)
            {
                _deviceInventory.RemoveKnownDevicesByEndpoint(disconnectedEndpoints);
            }

            await SyncConnectedDevicesAsync();

            StatusText = failedCount == 0
                ? $"Отключено: {disconnectedEndpoints.Count}"
                : $"Отключено: {disconnectedEndpoints.Count}, ошибок: {failedCount}";
        }
        catch (Exception ex)
        {
            StatusText = $"Ошибка: {ex.Message}";
        }
        finally
        {
            _isDisconnecting = false;
            NotifyCommandStateChanged();
        }
    }

    private async Task ConnectManualAsync()
    {
        if (!TryNormalizeEndpoint(EndpointInput, out var endpoint))
        {
            StatusText = "Неверный IP";
            return;
        }

        EnsureVisibleEndpoint(endpoint);

        if (IsConnected(endpoint))
        {
            MarkEndpointStatus(endpoint, "Уже подключено");
            SelectConnectedDevice(endpoint);
            StatusText = "Уже подключено";
            return;
        }

        try
        {
            _isConnecting = true;
            NotifyCommandStateChanged();
            StatusText = "Подключение...";

            var connectedDevices = await ConnectEndpointsAsync(
                [new ConnectRequest(endpoint, status => MarkEndpointStatus(endpoint, status))]);

            await SyncConnectedDevicesAsync();

            StatusText = connectedDevices.Count == 1
                ? "Подключено: 1"
                : "Подключиться не удалось";
        }
        catch (Exception ex)
        {
            StatusText = $"Ошибка: {ex.Message}";
        }
        finally
        {
            _isConnecting = false;
            NotifyCommandStateChanged();
        }
    }

    private async Task ToggleAutoConnectAsync(string? endpoint)
    {
        if (!CanToggleAutoConnect(endpoint))
        {
            return;
        }

        try
        {
            _isTogglingAutoConnect = true;
            NotifyCommandStateChanged();

            var isEnabled = !_autoConnectDevices.Contains(endpoint);
            await _autoConnectDevices.SetEnabledAsync(endpoint!, isEnabled);

            StatusText = isEnabled
                ? "Будет подключаться при запуске"
                : "Убрано из автоподключения";
        }
        catch (Exception ex)
        {
            StatusText = $"Ошибка: {ex.Message}";
        }
        finally
        {
            _isTogglingAutoConnect = false;
            NotifyCommandStateChanged();
        }
    }

    private async Task<List<TvDeviceProfile>> ConnectEndpointsAsync(IReadOnlyList<ConnectRequest> requests)
    {
        var connectedDevices = new List<TvDeviceProfile>();

        foreach (var request in requests)
        {
            if (IsConnected(request.Endpoint))
            {
                request.SetStatus("Уже подключено");
                connectedDevices.Add(CreateConnectedDevice(request.Endpoint));
                continue;
            }

            request.SetStatus("Подключение...");
            var result = await _adbConnectionService.ConnectAsync(request.Endpoint);
            request.SetStatus(result.Message);

            if (result.IsSuccess)
            {
                connectedDevices.Add(CreateConnectedDevice(request.Endpoint));
            }
        }

        if (connectedDevices.Count > 0)
        {
            _deviceInventory.UpsertKnownDevices(connectedDevices);
            _deviceInventory.ReplaceSelection(connectedDevices);
        }

        return connectedDevices;
    }

    private TvDeviceProfile CreateConnectedDevice(string endpoint)
    {
        var alias = _deviceAliases.GetAlias(endpoint);
        var displayName = string.IsNullOrWhiteSpace(alias)
            ? FindDiscoveredModel(endpoint) ?? endpoint
            : alias;

        return new TvDeviceProfile(
            endpoint,
            displayName,
            endpoint,
            DeviceConnectionKind.Network,
            DeviceReachability.Connected);
    }

    private string? FindDiscoveredModel(string endpoint)
    {
        return _endpointModels.GetValueOrDefault(endpoint);
    }

    /// <summary>
    /// Спрашивает модель у подключённых устройств. Сканер её не узнаёт, если на устройстве
    /// включена авторизация ADB, — зато уже открытый канал adb отвечает без вопросов.
    /// </summary>
    private async Task CacheConnectedModelsAsync(IReadOnlyList<string> endpoints)
    {
        var missingEndpoints = endpoints
            .Where(endpoint => !_endpointModels.ContainsKey(endpoint))
            .ToArray();

        if (missingEndpoints.Length == 0)
        {
            return;
        }

        var resolved = await Task.WhenAll(missingEndpoints.Select(async endpoint =>
            (Endpoint: endpoint, Model: await _adbConnectionService.GetDeviceModelAsync(endpoint))));

        foreach (var (endpoint, model) in resolved)
        {
            if (!string.IsNullOrWhiteSpace(model))
            {
                _endpointModels[endpoint] = model;
            }
        }
    }

    private bool IsConnected(string endpoint)
    {
        return _deviceInventory.KnownDevices.Any(x =>
            string.Equals(x.NetworkEndpoint, endpoint, StringComparison.OrdinalIgnoreCase) &&
            x.Reachability == DeviceReachability.Connected);
    }

    private static bool TryNormalizeEndpoint(string? value, out string endpoint)
    {
        endpoint = string.Empty;

        var normalized = value?.Trim();
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return false;
        }

        var separatorIndex = normalized.IndexOf(':');
        if (separatorIndex >= 0 && normalized.IndexOf(':', separatorIndex + 1) >= 0)
        {
            return false;
        }

        var host = normalized;
        var port = 5555;

        if (separatorIndex >= 0)
        {
            host = normalized[..separatorIndex];
            var rawPort = normalized[(separatorIndex + 1)..];
            if (string.IsNullOrWhiteSpace(rawPort) ||
                !int.TryParse(rawPort, NumberStyles.None, CultureInfo.InvariantCulture, out port) ||
                port is < 1 or > 65535)
            {
                return false;
            }
        }

        if (!IsValidIpv4(host))
        {
            return false;
        }

        endpoint = $"{host}:{port}";
        return true;
    }

    private static bool IsValidIpv4(string value)
    {
        var octets = value.Split('.', StringSplitOptions.None);
        if (octets.Length != 4)
        {
            return false;
        }

        foreach (var octet in octets)
        {
            if (octet.Length == 0 ||
                octet.Length > 3 ||
                !int.TryParse(octet, NumberStyles.None, CultureInfo.InvariantCulture, out var part) ||
                part is < 0 or > 255)
            {
                return false;
            }
        }

        return true;
    }

    private void OnDiscoveredDevicesChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        DiscoveredDevicesView.Refresh();
        OnPropertyChanged(nameof(DiscoverySummary));
    }

    private void OnSelectedCandidatesChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        OnPropertyChanged(nameof(SelectionSummary));
        NotifyCommandStateChanged();
    }

    private void OnKnownDevicesChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        foreach (var item in DiscoveredDevices)
        {
            item.Status = IsConnected(item.Endpoint) ? "Уже подключено" : "Готово";
        }

        NotifyCommandStateChanged();
    }

    private void NotifyCommandStateChanged()
    {
        RefreshCommand.NotifyCanExecuteChanged();
        StopScanCommand.NotifyCanExecuteChanged();
        ConnectSelectedCommand.NotifyCanExecuteChanged();
        ConnectManualCommand.NotifyCanExecuteChanged();
        DisconnectSelectedCommand.NotifyCanExecuteChanged();
        ToggleAutoConnectCommand.NotifyCanExecuteChanged();
        OnPropertyChanged(nameof(DiscoverySummary));
        OnPropertyChanged(nameof(SelectionSummary));
    }

    private bool CanConnectManual()
    {
        return !_isScanning &&
               !_isConnecting &&
               !_isDisconnecting &&
               !_isTogglingAutoConnect &&
               TryNormalizeEndpoint(EndpointInput, out _);
    }

    private bool CanConnectSelected()
    {
        return !_isScanning &&
               !_isConnecting &&
               !_isDisconnecting &&
               !_isTogglingAutoConnect &&
               SelectedCandidates.Any(x => !IsConnected(x.Endpoint));
    }

    private bool CanDisconnectSelected()
    {
        return !_isScanning &&
               !_isConnecting &&
               !_isDisconnecting &&
               !_isTogglingAutoConnect &&
               SelectedCandidates.Any(x => IsConnected(x.Endpoint));
    }

    private bool CanToggleAutoConnect(string? endpoint)
    {
        return !_isScanning &&
               !_isConnecting &&
               !_isDisconnecting &&
               !_isTogglingAutoConnect &&
               !string.IsNullOrWhiteSpace(endpoint);
    }

    private void EnsureVisibleEndpoint(string endpoint)
    {
        var existing = DiscoveredDevices.FirstOrDefault(x =>
            string.Equals(x.Endpoint, endpoint, StringComparison.OrdinalIgnoreCase));

        if (existing is not null)
        {
            return;
        }

        DiscoveredDevices.Insert(0, CreateDiscoveredItem(endpoint, "Готово"));
    }

    private void MarkEndpointStatus(string endpoint, string status)
    {
        var existing = DiscoveredDevices.FirstOrDefault(x =>
            string.Equals(x.Endpoint, endpoint, StringComparison.OrdinalIgnoreCase));

        if (existing is null)
        {
            DiscoveredDevices.Insert(0, CreateDiscoveredItem(endpoint, status));
            return;
        }

        existing.Status = status;
    }

    private void SelectConnectedDevice(string endpoint)
    {
        var device = _deviceInventory.KnownDevices.FirstOrDefault(x =>
            string.Equals(x.NetworkEndpoint, endpoint, StringComparison.OrdinalIgnoreCase));

        if (device is null)
        {
            return;
        }

        _deviceInventory.ReplaceSelection([device]);
    }

    private async Task<IReadOnlyList<string>> SyncConnectedDevicesAsync()
    {
        var connectedEndpoints = await _adbConnectionService.GetConnectedEndpointsAsync();
        _deviceInventory.RemoveDisconnectedNetworkDevices(connectedEndpoints);

        await CacheConnectedModelsAsync(connectedEndpoints);

        var adoptedDevices = connectedEndpoints
            .Where(endpoint => !IsConnected(endpoint))
            .Select(CreateConnectedDevice)
            .ToArray();

        if (adoptedDevices.Length > 0)
        {
            _deviceInventory.UpsertKnownDevices(adoptedDevices);
        }

        return connectedEndpoints;
    }

    private bool FilterDiscoveredDevice(object candidate)
    {
        if (candidate is not DiscoveredDeviceItemViewModel item)
        {
            return false;
        }

        if (string.IsNullOrWhiteSpace(EndpointInput))
        {
            return true;
        }

        var filter = EndpointInput.Trim();
        return item.Endpoint.Contains(filter, StringComparison.OrdinalIgnoreCase) ||
               item.Title.Contains(filter, StringComparison.OrdinalIgnoreCase) ||
               item.SecondaryText.Contains(filter, StringComparison.OrdinalIgnoreCase);
    }

    private DiscoveredDeviceItemViewModel CreateDiscoveredItem(
        string endpoint,
        string status,
        AdbEndpointState state = AdbEndpointState.Unknown,
        string? model = null)
    {
        if (!string.IsNullOrWhiteSpace(model))
        {
            _endpointModels[endpoint] = model;
        }

        var item = new DiscoveredDeviceItemViewModel(endpoint, status);
        item.ApplyDiscovery(model ?? FindDiscoveredModel(endpoint), state);
        item.ApplyAlias(_deviceAliases.GetAlias(endpoint));
        item.IsAutoConnectEnabled = _autoConnectDevices.Contains(endpoint);
        return item;
    }

    private void RefreshAutoConnectFlags()
    {
        foreach (var item in DiscoveredDevices)
        {
            item.IsAutoConnectEnabled = _autoConnectDevices.Contains(item.Endpoint);
        }
    }

    private void OnAliasesChanged(object? sender, EventArgs e)
    {
        foreach (var item in DiscoveredDevices)
        {
            item.ApplyAlias(_deviceAliases.GetAlias(item.Endpoint));
        }

        RefreshAutoConnectFlags();
        DiscoveredDevicesView.Refresh();
    }

    private void OnAutoConnectCatalogChanged(object? sender, EventArgs e)
    {
        RefreshAutoConnectFlags();
        NotifyCommandStateChanged();
    }

    private void BeginEditAlias(DiscoveredDeviceItemViewModel? row)
    {
        if (row is null)
        {
            return;
        }

        foreach (var item in DiscoveredDevices)
        {
            if (!ReferenceEquals(item, row) && item.IsEditingAlias)
            {
                item.CancelAliasEdit();
            }
        }

        row.BeginAliasEdit();
    }

    private async Task SaveAliasAsync(DiscoveredDeviceItemViewModel? row)
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

            await _deviceAliases.SetAliasAsync(row.Endpoint, alias);
            row.EndAliasEdit();
        }
        catch
        {
            row.CancelAliasEdit();
            StatusText = "Не удалось сохранить имя";
        }
    }

    private void CancelAliasEdit(DiscoveredDeviceItemViewModel? row)
    {
        row?.CancelAliasEdit();
    }
}

public sealed record ConnectRequest(string Endpoint, Action<string> SetStatus);

public sealed class DiscoveredDeviceItemViewModel : ObservableObject
{
    private string _status;
    private string _title;
    private string _secondaryText = string.Empty;
    private bool _hasSecondaryText;
    private bool _isAutoConnectEnabled;
    private string _aliasDraft = string.Empty;
    private bool _isEditingAlias;
    private string? _alias;
    private string? _model;
    private AdbEndpointState _state = AdbEndpointState.Unknown;

    public DiscoveredDeviceItemViewModel(string endpoint, string status)
    {
        Endpoint = endpoint;
        _status = status;
        _title = endpoint;
    }

    public string Endpoint { get; }

    /// <summary>Модель из ADB-баннера, если рукопожатие её вернуло.</summary>
    public string? Model => _model;

    public string Title
    {
        get => _title;
        private set => SetProperty(ref _title, value);
    }

    public string SecondaryText
    {
        get => _secondaryText;
        private set
        {
            if (!SetProperty(ref _secondaryText, value))
            {
                return;
            }

            HasSecondaryText = !string.IsNullOrWhiteSpace(value);
        }
    }

    public bool HasSecondaryText
    {
        get => _hasSecondaryText;
        private set => SetProperty(ref _hasSecondaryText, value);
    }

    public string Status
    {
        get => _status;
        set => SetProperty(ref _status, value);
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

    public bool IsAutoConnectEnabled
    {
        get => _isAutoConnectEnabled;
        set
        {
            if (SetProperty(ref _isAutoConnectEnabled, value))
            {
                OnPropertyChanged(nameof(AutoConnectToolTip));
            }
        }
    }

    public string AutoConnectToolTip => IsAutoConnectEnabled
        ? "Не подключать при запуске"
        : "Подключать при запуске";

    /// <summary>Результат ADB-рукопожатия: модель устройства и состояние порта.</summary>
    public void ApplyDiscovery(string? model, AdbEndpointState state)
    {
        _model = string.IsNullOrWhiteSpace(model) ? null : model.Trim();
        _state = state;
        UpdateDisplay();
    }

    public void ApplyAlias(string? alias)
    {
        _alias = string.IsNullOrWhiteSpace(alias) ? null : alias.Trim();
        UpdateDisplay();

        if (!IsEditingAlias)
        {
            AliasDraft = _alias ?? string.Empty;
        }
    }

    public void BeginAliasEdit()
    {
        AliasDraft = _alias ?? string.Empty;
        IsEditingAlias = true;
    }

    public void EndAliasEdit()
    {
        IsEditingAlias = false;
    }

    public void CancelAliasEdit()
    {
        AliasDraft = _alias ?? string.Empty;
        IsEditingAlias = false;
    }

    private void UpdateDisplay()
    {
        // Имя устройства: своё название важнее модели, модель важнее голого адреса.
        var name = _alias ?? _model;
        Title = name ?? Endpoint;

        var details = new List<string>(2);
        if (name is not null)
        {
            details.Add(Endpoint);
        }

        if (DescribeState(_state) is { } stateHint)
        {
            details.Add(stateHint);
        }

        SecondaryText = string.Join(" · ", details);
    }

    private static string? DescribeState(AdbEndpointState state)
    {
        return state switch
        {
            AdbEndpointState.PortOpen => "порт открыт, ADB не ответил",
            AdbEndpointState.AdbUnauthorized => "нужно подтвердить отладку на устройстве",
            AdbEndpointState.AdbTlsRequired => "нужен TLS, подключение через adb pair",
            _ => null
        };
    }
}
