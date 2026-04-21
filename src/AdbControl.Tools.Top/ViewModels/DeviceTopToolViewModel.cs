using System.Collections.ObjectModel;
using System.Collections.Specialized;
using AdbControl.Application.Common;
using AdbControl.Application.Devices;
using AdbControl.Application.Top;
using AdbControl.Core.Devices;

namespace AdbControl.Tools.Top.ViewModels;

public sealed class DeviceTopToolViewModel : ObservableObject, IDisposable
{
    private static readonly TimeSpan RefreshInterval = TimeSpan.FromSeconds(2);

    private readonly DeviceInventoryState _deviceInventory;
    private readonly DeviceAliasCatalog _deviceAliases;
    private readonly IDeviceTopService _deviceTopService;
    private TopDeviceOptionViewModel? _selectedDevice;
    private string _statusText;
    private string _summaryText = string.Empty;
    private string _rawOutput = string.Empty;
    private string _snapshotTimeText = "Снимков нет";
    private bool _isRefreshing;
    private bool _isRunning;
    private CancellationTokenSource? _pollingCts;
    private bool _isDisposed;

    public DeviceTopToolViewModel(
        DeviceInventoryState deviceInventory,
        DeviceAliasCatalog deviceAliases,
        IDeviceTopService deviceTopService)
    {
        _deviceInventory = deviceInventory;
        _deviceAliases = deviceAliases;
        _deviceTopService = deviceTopService;
        _statusText = "Устройство не выбрано";

        RefreshCommand = new RelayCommand(
            () => _ = RefreshAsync(),
            () => CanRefresh());

        StartCommand = new RelayCommand(
            () => _ = StartAsync(),
            () => CanStart());

        StopCommand = new RelayCommand(
            Stop,
            () => CanStop());

        _deviceInventory.KnownDevices.CollectionChanged += OnKnownDevicesChanged;
        _deviceAliases.Changed += OnAliasesChanged;

        RefreshConnectedDevices();
    }

    public ObservableCollection<TopDeviceOptionViewModel> ConnectedDevices { get; } = [];

    public ObservableCollection<TopProcessRowViewModel> Processes { get; } = [];

    public RelayCommand RefreshCommand { get; }

    public RelayCommand StartCommand { get; }

    public RelayCommand StopCommand { get; }

    public TopDeviceOptionViewModel? SelectedDevice
    {
        get => _selectedDevice;
        set
        {
            if (!SetProperty(ref _selectedDevice, value))
            {
                return;
            }

            Stop();
            ClearSnapshot();
            StatusText = value is null
                ? (ConnectedDevices.Count == 0 ? "Нет подключенных устройств" : "Устройство не выбрано")
                : "Нажми «Старт» или «Обновить»";

            OnPropertyChanged(nameof(EmptyStateMessage));
            NotifyCommandStateChanged();
        }
    }

    public string StatusText
    {
        get => _statusText;
        private set => SetProperty(ref _statusText, value);
    }

    public string SummaryText
    {
        get => _summaryText;
        private set
        {
            if (!SetProperty(ref _summaryText, value))
            {
                return;
            }

            OnPropertyChanged(nameof(HasSummary));
        }
    }

    public string RawOutput
    {
        get => _rawOutput;
        private set
        {
            if (!SetProperty(ref _rawOutput, value))
            {
                return;
            }

            OnPropertyChanged(nameof(HasRawOutput));
        }
    }

    public string SnapshotTimeText
    {
        get => _snapshotTimeText;
        private set => SetProperty(ref _snapshotTimeText, value);
    }

    public bool HasSummary => !string.IsNullOrWhiteSpace(SummaryText);

    public bool HasRawOutput => !string.IsNullOrWhiteSpace(RawOutput);

    public string DeviceSummary => ConnectedDevices.Count == 0
        ? "Нет подключенных устройств"
        : $"Подключено: {ConnectedDevices.Count}";

    public string ProcessSummary => Processes.Count == 0
        ? "Процессов нет"
        : $"Процессов: {Processes.Count}";

    public string EmptyStateMessage =>
        ConnectedDevices.Count == 0
            ? "Нет подключенных устройств."
            : SelectedDevice is null
                ? "Выбери устройство."
                : Processes.Count == 0
                    ? "Нажми «Старт» или «Обновить», чтобы получить top."
                    : string.Empty;

    private async Task RefreshAsync()
    {
        if (SelectedDevice is null || _isDisposed)
        {
            return;
        }

        await CaptureSnapshotAsync(SelectedDevice, CancellationToken.None);
    }

    private async Task StartAsync()
    {
        if (SelectedDevice is null || _isRunning || _isDisposed)
        {
            return;
        }

        Stop();

        var cts = new CancellationTokenSource();
        _pollingCts = cts;
        _isRunning = true;
        NotifyCommandStateChanged();

        try
        {
            while (!cts.IsCancellationRequested && !_isDisposed)
            {
                var currentSelection = SelectedDevice;
                if (currentSelection is null)
                {
                    break;
                }

                await CaptureSnapshotAsync(currentSelection, cts.Token);
                await Task.Delay(RefreshInterval, cts.Token);
            }
        }
        catch (OperationCanceledException)
        {
        }
        finally
        {
            if (ReferenceEquals(_pollingCts, cts))
            {
                _pollingCts = null;
                _isRunning = false;
                if (!_isDisposed)
                {
                    StatusText = SelectedDevice is null
                        ? (ConnectedDevices.Count == 0 ? "Нет подключенных устройств" : "Устройство не выбрано")
                        : Processes.Count > 0
                            ? "Остановлено"
                            : "Нажми «Старт» или «Обновить»";
                    NotifyCommandStateChanged();
                }
            }

            cts.Dispose();
        }
    }

    private void Stop()
    {
        _pollingCts?.Cancel();
    }

    private async Task CaptureSnapshotAsync(TopDeviceOptionViewModel deviceOption, CancellationToken cancellationToken)
    {
        try
        {
            _isRefreshing = true;
            StatusText = _isRunning ? "Обновляется..." : "Загрузка...";
            NotifyCommandStateChanged();

            var result = await _deviceTopService.CaptureAsync(deviceOption.Device, cancellationToken);
            if (cancellationToken.IsCancellationRequested || _isDisposed)
            {
                return;
            }

            if (SelectedDevice is null || !string.Equals(SelectedDevice.TargetId, deviceOption.TargetId, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            if (!result.IsSuccess || result.Snapshot is null)
            {
                ClearSnapshot();
                StatusText = string.IsNullOrWhiteSpace(result.Message) ? "Не удалось получить top" : result.Message;
                OnPropertyChanged(nameof(EmptyStateMessage));
                return;
            }

            ApplySnapshot(result.Snapshot);
            StatusText = _isRunning ? "Онлайн" : "Готово";
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            if (!_isDisposed)
            {
                ClearSnapshot();
                StatusText = ex.Message;
                OnPropertyChanged(nameof(EmptyStateMessage));
            }
        }
        finally
        {
            _isRefreshing = false;
            NotifyCommandStateChanged();
        }
    }

    private void ApplySnapshot(DeviceTopSnapshot snapshot)
    {
        SummaryText = snapshot.SummaryText;
        RawOutput = snapshot.RawOutput;
        SnapshotTimeText = $"Обновлено: {snapshot.CapturedAt:dd.MM.yyyy HH:mm:ss}";

        Processes.Clear();
        foreach (var process in snapshot.Processes)
        {
            Processes.Add(new TopProcessRowViewModel(process.Pid, process.Cpu, process.Res, process.State, process.Name));
        }

        OnPropertyChanged(nameof(ProcessSummary));
        OnPropertyChanged(nameof(EmptyStateMessage));
    }

    private void ClearSnapshot()
    {
        SummaryText = string.Empty;
        RawOutput = string.Empty;
        SnapshotTimeText = "Снимков нет";
        Processes.Clear();
        OnPropertyChanged(nameof(ProcessSummary));
        OnPropertyChanged(nameof(EmptyStateMessage));
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

        SelectedDevice = nextSelected;

        OnPropertyChanged(nameof(DeviceSummary));
    }

    private TopDeviceOptionViewModel BuildDeviceOption(TvDeviceProfile device)
    {
        var targetId = GetTargetId(device);
        var alias = _deviceAliases.GetAlias(targetId);
        var displayText = string.IsNullOrWhiteSpace(alias)
            ? device.DisplayName
            : $"{alias} ({targetId})";

        return new TopDeviceOptionViewModel(device, targetId, displayText);
    }

    private static string GetTargetId(TvDeviceProfile device)
    {
        return string.IsNullOrWhiteSpace(device.NetworkEndpoint)
            ? device.Id
            : device.NetworkEndpoint;
    }

    private bool CanRefresh()
    {
        return !_isDisposed && !_isRefreshing && !_isRunning && SelectedDevice is not null;
    }

    private bool CanStart()
    {
        return !_isDisposed && !_isRefreshing && !_isRunning && SelectedDevice is not null;
    }

    private bool CanStop()
    {
        return !_isDisposed && _isRunning;
    }

    private void NotifyCommandStateChanged()
    {
        RefreshCommand.NotifyCanExecuteChanged();
        StartCommand.NotifyCanExecuteChanged();
        StopCommand.NotifyCanExecuteChanged();
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
        _deviceInventory.KnownDevices.CollectionChanged -= OnKnownDevicesChanged;
        _deviceAliases.Changed -= OnAliasesChanged;
        Stop();
        _pollingCts?.Dispose();
        _pollingCts = null;
    }
}

public sealed record TopDeviceOptionViewModel(TvDeviceProfile Device, string TargetId, string DisplayText)
{
    public override string ToString() => DisplayText;
}

public sealed record TopProcessRowViewModel(string Pid, string Cpu, string Res, string State, string Name);
