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
    private bool _isScanning;
    private bool _isConnecting;
    private bool _isDisconnecting;
    private string _statusText = string.Empty;
    private string _endpointInput = string.Empty;

    public DeviceConnectToolViewModel(
        DeviceInventoryState deviceInventory,
        IDeviceDiscoveryService deviceDiscoveryService,
        IAdbConnectionService adbConnectionService,
        DeviceAliasCatalog deviceAliases)
    {
        _deviceInventory = deviceInventory;
        _deviceDiscoveryService = deviceDiscoveryService;
        _adbConnectionService = adbConnectionService;
        _deviceAliases = deviceAliases;

        DiscoveredDevicesView = CollectionViewSource.GetDefaultView(DiscoveredDevices);
        DiscoveredDevicesView.Filter = FilterDiscoveredDevice;

        RefreshCommand = new RelayCommand(
            () => _ = RefreshAsync(),
            () => !_isScanning && !_isConnecting && !_isDisconnecting);

        ConnectSelectedCommand = new RelayCommand(
            () => _ = ConnectSelectedAsync(),
            () => CanConnectSelected());

        ConnectManualCommand = new RelayCommand(
            () => _ = ConnectManualAsync(),
            () => CanConnectManual());

        DisconnectSelectedCommand = new RelayCommand(
            () => _ = DisconnectSelectedAsync(),
            () => CanDisconnectSelected());

        DiscoveredDevices.CollectionChanged += OnDiscoveredDevicesChanged;
        SelectedCandidates.CollectionChanged += OnSelectedCandidatesChanged;
        _deviceInventory.KnownDevices.CollectionChanged += OnKnownDevicesChanged;
        _deviceAliases.Changed += OnAliasesChanged;

        _ = RefreshAsync();
    }

    public ObservableCollection<DiscoveredDeviceItemViewModel> DiscoveredDevices { get; } = [];

    public ObservableCollection<DiscoveredDeviceItemViewModel> SelectedCandidates { get; } = [];

    public ICollectionView DiscoveredDevicesView { get; }

    public RelayCommand RefreshCommand { get; }

    public RelayCommand ConnectSelectedCommand { get; }

    public RelayCommand ConnectManualCommand { get; }

    public RelayCommand DisconnectSelectedCommand { get; }

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

    public string DiscoverySummary
    {
        get
        {
            if (_isScanning)
            {
                return "Поиск...";
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
        try
        {
            _isScanning = true;
            NotifyCommandStateChanged();

            SelectedCandidates.Clear();
            DiscoveredDevices.Clear();
            StatusText = "Поиск...";

            await PruneDisconnectedDevicesAsync();
            var discoveredEndpoints = await _deviceDiscoveryService.DiscoverAsync();
            foreach (var endpoint in discoveredEndpoints.Select(x => x.Endpoint))
            {
                DiscoveredDevices.Add(CreateDiscoveredItem(
                    endpoint,
                    IsConnected(endpoint) ? "Уже подключено" : "Готово"));
            }

            StatusText = string.Empty;
        }
        catch (Exception ex)
        {
            StatusText = $"Ошибка: {ex.Message}";
        }
        finally
        {
            _isScanning = false;
            NotifyCommandStateChanged();
        }
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

            await PruneDisconnectedDevicesAsync();

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

            await PruneDisconnectedDevicesAsync();

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

            await PruneDisconnectedDevicesAsync();

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
        return new TvDeviceProfile(
            endpoint,
            string.IsNullOrWhiteSpace(alias) ? endpoint : alias,
            endpoint,
            DeviceConnectionKind.Network,
            DeviceReachability.Connected);
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
        ConnectSelectedCommand.NotifyCanExecuteChanged();
        ConnectManualCommand.NotifyCanExecuteChanged();
        DisconnectSelectedCommand.NotifyCanExecuteChanged();
        OnPropertyChanged(nameof(DiscoverySummary));
        OnPropertyChanged(nameof(SelectionSummary));
    }

    private bool CanConnectManual()
    {
        return !_isScanning &&
               !_isConnecting &&
               !_isDisconnecting &&
               TryNormalizeEndpoint(EndpointInput, out _);
    }

    private bool CanConnectSelected()
    {
        return !_isScanning &&
               !_isConnecting &&
               !_isDisconnecting &&
               SelectedCandidates.Any(x => !IsConnected(x.Endpoint));
    }

    private bool CanDisconnectSelected()
    {
        return !_isScanning &&
               !_isConnecting &&
               !_isDisconnecting &&
               SelectedCandidates.Any(x => IsConnected(x.Endpoint));
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

    private async Task PruneDisconnectedDevicesAsync()
    {
        var connectedEndpoints = await _adbConnectionService.GetConnectedEndpointsAsync();
        _deviceInventory.RemoveDisconnectedNetworkDevices(connectedEndpoints);
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

    private DiscoveredDeviceItemViewModel CreateDiscoveredItem(string endpoint, string status)
    {
        var item = new DiscoveredDeviceItemViewModel(endpoint, status);
        item.ApplyAlias(_deviceAliases.GetAlias(endpoint));
        return item;
    }

    private void OnAliasesChanged(object? sender, EventArgs e)
    {
        foreach (var item in DiscoveredDevices)
        {
            item.ApplyAlias(_deviceAliases.GetAlias(item.Endpoint));
        }

        DiscoveredDevicesView.Refresh();
    }
}

public sealed record ConnectRequest(string Endpoint, Action<string> SetStatus);

public sealed class DiscoveredDeviceItemViewModel : ObservableObject
{
    private string _status;
    private string _title;
    private string _secondaryText = string.Empty;
    private bool _hasSecondaryText;

    public DiscoveredDeviceItemViewModel(string endpoint, string status)
    {
        Endpoint = endpoint;
        _status = status;
        _title = endpoint;
    }

    public string Endpoint { get; }

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

    public void ApplyAlias(string? alias)
    {
        var normalizedAlias = string.IsNullOrWhiteSpace(alias)
            ? null
            : alias.Trim();

        Title = normalizedAlias ?? Endpoint;
        SecondaryText = normalizedAlias is null
            ? string.Empty
            : Endpoint;
    }
}
