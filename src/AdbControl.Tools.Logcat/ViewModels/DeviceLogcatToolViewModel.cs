using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Collections.Concurrent;
using System.Text;
using System.Windows;
using System.Windows.Threading;
using AdbControl.Application.Common;
using AdbControl.Application.Devices;
using AdbControl.Application.Logcat;
using AdbControl.Core.Devices;

namespace AdbControl.Tools.Logcat.ViewModels;

public sealed class DeviceLogcatToolViewModel : ObservableObject, IDisposable
{
    private const int MaxLines = 4000;

    private readonly DeviceInventoryState _deviceInventory;
    private readonly DeviceAliasCatalog _deviceAliases;
    private readonly IDeviceLogcatService _deviceLogcatService;
    private readonly ConcurrentQueue<LogcatOutputLine> _pendingLines = new();
    private readonly DispatcherTimer _flushTimer;
    private readonly List<LogcatLineItemViewModel> _allLines = [];
    private readonly StringBuilder _visibleTextBuilder = new();
    private LogcatDeviceOptionViewModel? _selectedDevice;
    private IDeviceLogcatSession? _session;
    private string _filterArguments = string.Empty;
    private string _searchText = string.Empty;
    private string _statusText;
    private string _visibleText = string.Empty;
    private bool _isBusy;
    private bool _isRunning;
    private bool _isDisposed;

    public DeviceLogcatToolViewModel(
        DeviceInventoryState deviceInventory,
        DeviceAliasCatalog deviceAliases,
        IDeviceLogcatService deviceLogcatService)
    {
        _deviceInventory = deviceInventory;
        _deviceAliases = deviceAliases;
        _deviceLogcatService = deviceLogcatService;
        _statusText = "Устройство не выбрано";
        _flushTimer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromMilliseconds(120)
        };
        _flushTimer.Tick += OnFlushTimerTick;

        StartCommand = new RelayCommand(
            () => _ = StartAsync(),
            () => CanStart());
        StopCommand = new RelayCommand(
            () => _ = StopAsync(),
            () => CanStop());
        ClearScreenCommand = new RelayCommand(
            ClearScreen,
            () => CanClearScreen());
        ClearBufferCommand = new RelayCommand(
            () => _ = ClearBufferAsync(),
            () => CanClearBuffer());

        _deviceInventory.KnownDevices.CollectionChanged += OnKnownDevicesChanged;
        _deviceAliases.Changed += OnAliasesChanged;

        RefreshConnectedDevices();
        _flushTimer.Start();
    }

    public ObservableCollection<LogcatDeviceOptionViewModel> ConnectedDevices { get; } = [];

    public ObservableCollection<LogcatLineItemViewModel> VisibleLines { get; } = [];

    public RelayCommand StartCommand { get; }

    public RelayCommand StopCommand { get; }

    public RelayCommand ClearScreenCommand { get; }

    public RelayCommand ClearBufferCommand { get; }

    public LogcatDeviceOptionViewModel? SelectedDevice
    {
        get => _selectedDevice;
        set
        {
            if (!SetProperty(ref _selectedDevice, value))
            {
                return;
            }

            _ = StopAsync();
            ClearLines();
            StatusText = value is null
                ? (ConnectedDevices.Count == 0 ? "Нет подключённых устройств" : "Устройство не выбрано")
                : "Нажми «Старт»";
            OnPropertyChanged(nameof(EmptyStateMessage));
            NotifyCommandStateChanged();
        }
    }

    public string FilterArguments
    {
        get => _filterArguments;
        set => SetProperty(ref _filterArguments, value);
    }

    public string SearchText
    {
        get => _searchText;
        set
        {
            if (SetProperty(ref _searchText, value))
            {
                RebuildVisibleLines();
            }
        }
    }

    public string StatusText
    {
        get => _statusText;
        private set => SetProperty(ref _statusText, value);
    }

    public string VisibleText
    {
        get => _visibleText;
        private set => SetProperty(ref _visibleText, value);
    }

    public string DeviceSummary => ConnectedDevices.Count == 0
        ? "Нет подключённых устройств"
        : ConnectedDevices.Count == 1
            ? "1 устройство"
            : $"Устройств: {ConnectedDevices.Count}";

    public string LineSummary => VisibleLines.Count == 0
        ? "Строк: 0"
        : $"Строк: {VisibleLines.Count}";

    public bool HasVisibleLines => VisibleLines.Count > 0;

    public string EmptyStateMessage =>
        ConnectedDevices.Count == 0
            ? "Нет подключённых устройств."
            : SelectedDevice is null
                ? "Выбери телевизор."
                : _isRunning
                    ? "Жду строки logcat..."
                    : "Нажми «Старт», чтобы начать.";

    private async Task StartAsync()
    {
        if (SelectedDevice is null || _isDisposed)
        {
            return;
        }

        await StopAsync();
        ClearLines();

        try
        {
            _isBusy = true;
            StatusText = "Запуск...";
            NotifyCommandStateChanged();

            var result = await _deviceLogcatService.StartSessionAsync(
                SelectedDevice.Device,
                FilterArguments,
                HandleIncomingLine);

            if (!result.IsSuccess || result.Session is null)
            {
                StatusText = string.IsNullOrWhiteSpace(result.Message) ? "Не удалось запустить logcat." : result.Message;
                return;
            }

            _session = result.Session;
            _isRunning = true;
            StatusText = "Онлайн";
        }
        catch (Exception ex)
        {
            StatusText = ex.Message;
        }
        finally
        {
            _isBusy = false;
            OnPropertyChanged(nameof(EmptyStateMessage));
            NotifyCommandStateChanged();
        }
    }

    private async Task StopAsync()
    {
        if (_session is null)
        {
            _isRunning = false;
            NotifyCommandStateChanged();
            return;
        }

        try
        {
            _isBusy = true;
            NotifyCommandStateChanged();
            await _session.StopAsync();
            await _session.DisposeAsync();
        }
        catch
        {
            // Ignore stop failures.
        }
        finally
        {
            _session = null;
            _isBusy = false;
            _isRunning = false;
            StatusText = SelectedDevice is null
                ? (ConnectedDevices.Count == 0 ? "Нет подключённых устройств" : "Устройство не выбрано")
                : "Остановлено";
            OnPropertyChanged(nameof(EmptyStateMessage));
            NotifyCommandStateChanged();
        }
    }

    private async Task ClearBufferAsync()
    {
        if (SelectedDevice is null)
        {
            return;
        }

        try
        {
            _isBusy = true;
            StatusText = "Очистка...";
            NotifyCommandStateChanged();

            var result = await _deviceLogcatService.ClearBufferAsync(SelectedDevice.Device);
            StatusText = result.Message;
        }
        catch (Exception ex)
        {
            StatusText = ex.Message;
        }
        finally
        {
            _isBusy = false;
            OnPropertyChanged(nameof(EmptyStateMessage));
            NotifyCommandStateChanged();
        }
    }

    private void ClearScreen()
    {
        ClearLines();

        StatusText = _isRunning
            ? "Онлайн"
            : SelectedDevice is null
                ? (ConnectedDevices.Count == 0 ? "Нет подключённых устройств" : "Устройство не выбрано")
                : "Экран очищен";

        NotifyCommandStateChanged();
    }

    private void HandleIncomingLine(LogcatOutputLine line)
    {
        if (_isDisposed)
        {
            return;
        }

        _pendingLines.Enqueue(line);
    }

    private void OnFlushTimerTick(object? sender, EventArgs e)
    {
        FlushPendingLines();
    }

    private void FlushPendingLines()
    {
        if (_pendingLines.IsEmpty)
        {
            return;
        }

        var addedVisibleLines = new List<LogcatLineItemViewModel>();
        var removedVisibleLine = false;
        var useIncrementalAppend = string.IsNullOrWhiteSpace(SearchText);

        while (_pendingLines.TryDequeue(out var line))
        {
            var item = new LogcatLineItemViewModel(line.Text, line.IsError);
            _allLines.Add(item);

            if (MatchesSearch(item))
            {
                VisibleLines.Add(item);
                addedVisibleLines.Add(item);
            }

            while (_allLines.Count > MaxLines)
            {
                var removed = _allLines[0];
                _allLines.RemoveAt(0);
                if (VisibleLines.Remove(removed))
                {
                    removedVisibleLine = true;
                }
            }
        }

        if (useIncrementalAppend && !removedVisibleLine)
        {
            foreach (var item in addedVisibleLines)
            {
                AppendVisibleLineText(item.Text);
            }

            VisibleText = _visibleTextBuilder.ToString();
        }
        else
        {
            RebuildVisibleText();
        }

        OnPropertyChanged(nameof(LineSummary));
        OnPropertyChanged(nameof(HasVisibleLines));
        OnPropertyChanged(nameof(EmptyStateMessage));
        NotifyCommandStateChanged();
    }

    private void RebuildVisibleLines()
    {
        VisibleLines.Clear();

        foreach (var item in _allLines.Where(MatchesSearch))
        {
            VisibleLines.Add(item);
        }

        RebuildVisibleText();
        OnPropertyChanged(nameof(LineSummary));
        OnPropertyChanged(nameof(HasVisibleLines));
        OnPropertyChanged(nameof(EmptyStateMessage));
        NotifyCommandStateChanged();
    }

    private bool MatchesSearch(LogcatLineItemViewModel item)
    {
        var search = SearchText?.Trim();
        return string.IsNullOrWhiteSpace(search) ||
               item.Text.Contains(search, StringComparison.OrdinalIgnoreCase);
    }

    private void ClearLines()
    {
        while (_pendingLines.TryDequeue(out _))
        {
        }

        _allLines.Clear();
        VisibleLines.Clear();
        _visibleTextBuilder.Clear();
        VisibleText = string.Empty;
        OnPropertyChanged(nameof(LineSummary));
        OnPropertyChanged(nameof(HasVisibleLines));
        OnPropertyChanged(nameof(EmptyStateMessage));
        NotifyCommandStateChanged();
    }

    private void AppendVisibleLineText(string text)
    {
        if (_visibleTextBuilder.Length > 0)
        {
            _visibleTextBuilder.AppendLine();
        }

        _visibleTextBuilder.Append(text);
    }

    private void RebuildVisibleText()
    {
        _visibleTextBuilder.Clear();

        foreach (var item in VisibleLines)
        {
            AppendVisibleLineText(item.Text);
        }

        VisibleText = _visibleTextBuilder.ToString();
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

    private LogcatDeviceOptionViewModel BuildDeviceOption(TvDeviceProfile device)
    {
        var targetId = GetTargetId(device);
        var alias = _deviceAliases.GetAlias(targetId);
        var displayText = string.IsNullOrWhiteSpace(alias)
            ? device.DisplayName
            : alias;

        return new LogcatDeviceOptionViewModel(device, targetId, displayText);
    }

    private static string GetTargetId(TvDeviceProfile device)
    {
        return string.IsNullOrWhiteSpace(device.NetworkEndpoint)
            ? device.Id
            : device.NetworkEndpoint;
    }

    private bool CanStart()
    {
        return !_isDisposed && !_isBusy && !_isRunning && SelectedDevice is not null;
    }

    private bool CanStop()
    {
        return !_isDisposed && !_isBusy && _session is not null;
    }

    private bool CanClearScreen()
    {
        return !_isDisposed && !_isBusy && _allLines.Count > 0;
    }

    private bool CanClearBuffer()
    {
        return !_isDisposed && !_isBusy && SelectedDevice is not null;
    }

    private void NotifyCommandStateChanged()
    {
        StartCommand.NotifyCanExecuteChanged();
        StopCommand.NotifyCanExecuteChanged();
        ClearScreenCommand.NotifyCanExecuteChanged();
        ClearBufferCommand.NotifyCanExecuteChanged();
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
        _flushTimer.Stop();
        _flushTimer.Tick -= OnFlushTimerTick;
        _deviceInventory.KnownDevices.CollectionChanged -= OnKnownDevicesChanged;
        _deviceAliases.Changed -= OnAliasesChanged;
        _ = StopAsync();
    }
}

public sealed record LogcatDeviceOptionViewModel(TvDeviceProfile Device, string TargetId, string DisplayText)
{
    public override string ToString() => DisplayText;
}

public sealed record LogcatLineItemViewModel(string Text, bool IsError);
