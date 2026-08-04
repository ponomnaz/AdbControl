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
    /// <summary>
    /// Около часа живого потока на телевизоре. Строки виртуализованы, так что
    /// цена буфера — только память под сами строки.
    /// </summary>
    private const int MaxLines = 50_000;
    private const string NetariumPackageName = "cs.netarium";

    /// <summary>Пауза перед перезапуском: иначе сессия дёргалась бы на каждой букве.</summary>
    private static readonly TimeSpan FilterRestartDelay = TimeSpan.FromMilliseconds(900);

    /// <summary>Как часто проверять, не сменился ли pid приложения.</summary>
    private static readonly TimeSpan ProcessIdPollInterval = TimeSpan.FromSeconds(4);

    private readonly DeviceInventoryState _deviceInventory;
    private readonly DeviceAliasCatalog _deviceAliases;
    private readonly IDeviceLogcatService _deviceLogcatService;
    private readonly ConcurrentQueue<LogcatOutputLine> _pendingLines = new();
    private readonly DispatcherTimer _flushTimer;
    private readonly Queue<LogcatLineItemViewModel> _allLines = new();
    private readonly LogcatSettingsCatalog _logcatSettings;
    private readonly DispatcherTimer _filterRestartTimer;
    private readonly DispatcherTimer _processIdTimer;
    private LogcatDeviceOptionViewModel? _selectedDevice;
    private IDeviceLogcatSession? _session;
    private LogcatSearchTerms _activeTerms = LogcatSearchTerms.Empty;
    private LogcatLevelOptionViewModel _selectedLevel;
    private string _extraArguments = string.Empty;
    private bool _onlyNetarium;
    private int? _sessionProcessId;
    private bool _isApplyingStoredSettings;
    private string _statusText;
    private bool _isBusy;
    private bool _isRunning;
    private bool _isDisposed;

    public DeviceLogcatToolViewModel(
        DeviceInventoryState deviceInventory,
        DeviceAliasCatalog deviceAliases,
        IDeviceLogcatService deviceLogcatService,
        LogcatSettingsCatalog logcatSettings)
    {
        _deviceInventory = deviceInventory;
        _deviceAliases = deviceAliases;
        _deviceLogcatService = deviceLogcatService;
        _logcatSettings = logcatSettings;
        _statusText = "Устройство не выбрано";
        _selectedLevel = LogcatLevelOptionViewModel.All[0];
        _flushTimer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromMilliseconds(120)
        };
        _flushTimer.Tick += OnFlushTimerTick;

        _filterRestartTimer = new DispatcherTimer { Interval = FilterRestartDelay };
        _filterRestartTimer.Tick += OnFilterRestartTimerTick;

        _processIdTimer = new DispatcherTimer { Interval = ProcessIdPollInterval };
        _processIdTimer.Tick += OnProcessIdTimerTick;

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
        CopySelectedCommand = new RelayCommand(
            CopySelectedLines,
            () => VisibleLines.Count > 0);

        _deviceInventory.KnownDevices.CollectionChanged += OnKnownDevicesChanged;
        _deviceAliases.Changed += OnAliasesChanged;
        _logcatSettings.Changed += OnStoredSettingsChanged;

        ApplyStoredSettings();
        IncludeTerms.CollectionChanged += OnSearchTermsChanged;
        ExcludeTerms.CollectionChanged += OnSearchTermsChanged;
        FilterTags.CollectionChanged += OnFilterTagsChanged;

        RefreshConnectedDevices();
        _flushTimer.Start();
    }

    public ObservableCollection<LogcatDeviceOptionViewModel> ConnectedDevices { get; } = [];

    public RangeObservableCollection<LogcatLineItemViewModel> VisibleLines { get; } = [];

    public ObservableCollection<LogcatLineItemViewModel> SelectedLines { get; } = [];

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

    public IReadOnlyList<LogcatLevelOptionViewModel> Levels => LogcatLevelOptionViewModel.All;

    /// <summary>Теги фильтра устройства. Условия с двоеточием и пробелами logcat не принимает.</summary>
    public ObservableCollection<string> FilterTags { get; } = [];

    public LogcatLevelOptionViewModel SelectedLevel
    {
        get => _selectedLevel;
        set
        {
            if (SetProperty(ref _selectedLevel, value))
            {
                OnFilterChanged(restartImmediately: true);
            }
        }
    }

    public bool OnlyNetarium
    {
        get => _onlyNetarium;
        set
        {
            if (SetProperty(ref _onlyNetarium, value))
            {
                OnFilterChanged(restartImmediately: true);
            }
        }
    }

    /// <summary>Сырые аргументы logcat для редких случаев, подставляются последними.</summary>
    public string ExtraArguments
    {
        get => _extraArguments;
        set
        {
            if (SetProperty(ref _extraArguments, value))
            {
                OnFilterChanged(restartImmediately: false);
            }
        }
    }

    /// <summary>Строка проходит, если содержит хотя бы одно условие. Пусто — проходят все.</summary>
    public ObservableCollection<string> IncludeTerms { get; } = [];

    /// <summary>Строка отбрасывается, если содержит хотя бы одно условие. Сильнее <see cref="IncludeTerms"/>.</summary>
    public ObservableCollection<string> ExcludeTerms { get; } = [];

    public string StatusText
    {
        get => _statusText;
        private set => SetProperty(ref _statusText, value);
    }

    public RelayCommand CopySelectedCommand { get; }

    public string DeviceSummary => ConnectedDevices.Count == 0
        ? "Нет подключённых устройств"
        : ConnectedDevices.Count == 1
            ? "1 устройство"
            : $"Устройств: {ConnectedDevices.Count}";

    /// <summary>
    /// При активных условиях показываем и видимые, и общее число: иначе кажется,
    /// будто строки пропали, хотя они просто отфильтрованы.
    /// </summary>
    public string LineSummary => HasSearchTerms
        ? $"Строк: {VisibleLines.Count} из {_allLines.Count}"
        : $"Строк: {VisibleLines.Count}";

    private bool HasSearchTerms => !_activeTerms.IsEmpty;

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

            _sessionProcessId = OnlyNetarium
                ? await _deviceLogcatService.GetProcessIdAsync(SelectedDevice.Device, NetariumPackageName)
                : null;

            if (OnlyNetarium && _sessionProcessId is null)
            {
                StatusText = $"{NetariumPackageName} не запущен — фильтр по приложению применить не к чему.";
                return;
            }

            var result = await _deviceLogcatService.StartSessionAsync(
                SelectedDevice.Device,
                BuildFilterSettings().BuildArguments(_sessionProcessId),
                HandleIncomingLine);

            if (!result.IsSuccess || result.Session is null)
            {
                StatusText = string.IsNullOrWhiteSpace(result.Message) ? "Не удалось запустить logcat." : result.Message;
                return;
            }

            _session = result.Session;
            _isRunning = true;
            StatusText = "Онлайн";

            // pid живёт до перезапуска приложения — следим, пока фильтр по нему включён.
            if (OnlyNetarium)
            {
                _processIdTimer.Start();
            }
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
        _processIdTimer.Stop();

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

        while (_pendingLines.TryDequeue(out var line))
        {
            var item = new LogcatLineItemViewModel(line.Text, line.IsError);
            _allLines.Enqueue(item);

            if (MatchesSearch(item))
            {
                VisibleLines.Add(item);
            }

            while (_allLines.Count > MaxLines)
            {
                var removed = _allLines.Dequeue();

                // Видимые строки идут в том же порядке, что и общий буфер,
                // поэтому вытесняемая, если она видима, всегда стоит первой.
                if (VisibleLines.Count > 0 && ReferenceEquals(VisibleLines[0], removed))
                {
                    VisibleLines.RemoveAt(0);
                }
            }
        }

        OnPropertyChanged(nameof(LineSummary));
        OnPropertyChanged(nameof(HasVisibleLines));
        OnPropertyChanged(nameof(EmptyStateMessage));
        NotifyCommandStateChanged();
    }

    private void RebuildVisibleLines()
    {
        VisibleLines.ReplaceAll(_allLines.Where(MatchesSearch));

        OnPropertyChanged(nameof(LineSummary));
        OnPropertyChanged(nameof(HasVisibleLines));
        OnPropertyChanged(nameof(EmptyStateMessage));
        NotifyCommandStateChanged();
    }

    private bool MatchesSearch(LogcatLineItemViewModel item)
    {
        return _activeTerms.Matches(item.Text);
    }

    private void OnSearchTermsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        // Слепок условий, чтобы не обходить наблюдаемые коллекции на каждой строке.
        _activeTerms = new LogcatSearchTerms(IncludeTerms.ToArray(), ExcludeTerms.ToArray());

        RebuildVisibleLines();
        PersistSettings();
    }

    /// <summary>
    /// Теги с двоеточием или пробелом logcat отвергает целиком («Invalid filter expression»),
    /// поэтому такой тег не принимаем и объясняем, чем его заменить.
    /// </summary>
    private void OnFilterTagsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.NewItems is not null)
        {
            var rejected = e.NewItems
                .OfType<string>()
                .Where(tag => !LogcatFilterSettings.IsValidTag(tag))
                .ToArray();

            if (rejected.Length > 0)
            {
                foreach (var tag in rejected)
                {
                    FilterTags.Remove(tag);
                }

                StatusText = $"Тег «{rejected[0]}» logcat не принимает: в нём двоеточие или пробел. Такие строки убирай через «Скрывать».";
                return;
            }
        }

        OnFilterChanged(restartImmediately: true);
    }

    private void OnFilterChanged(bool restartImmediately)
    {
        if (_isApplyingStoredSettings)
        {
            return;
        }

        PersistSettings();

        _filterRestartTimer.Stop();

        if (!_isRunning)
        {
            return;
        }

        if (restartImmediately)
        {
            _ = RestartForFilterAsync();
            return;
        }

        _filterRestartTimer.Start();
    }

    private void OnFilterRestartTimerTick(object? sender, EventArgs e)
    {
        _filterRestartTimer.Stop();
        _ = RestartForFilterAsync();
    }

    /// <summary>
    /// Экран очищается намеренно: logcat заново выдаст весь буфер устройства,
    /// уже отфильтрованным, и смешивать его со старой выборкой нельзя.
    /// </summary>
    private async Task RestartForFilterAsync()
    {
        if (_isDisposed || SelectedDevice is null)
        {
            return;
        }

        await StopAsync();
        await StartAsync();
    }

    private async void OnProcessIdTimerTick(object? sender, EventArgs e)
    {
        if (_isDisposed || !_isRunning || !OnlyNetarium || SelectedDevice is null)
        {
            return;
        }

        try
        {
            var currentProcessId = await _deviceLogcatService.GetProcessIdAsync(
                SelectedDevice.Device,
                NetariumPackageName);

            if (currentProcessId is null || currentProcessId == _sessionProcessId)
            {
                return;
            }

            StatusText = "Netarium перезапустился — переподключаю лог";
            await RestartForFilterAsync();
        }
        catch
        {
            // Слежение за pid не должно ронять сессию.
        }
    }

    private LogcatFilterSettings BuildFilterSettings()
    {
        return new LogcatFilterSettings(
            SelectedLevel.Level,
            FilterTags.ToArray(),
            OnlyNetarium,
            ExtraArguments);
    }

    private void PersistSettings()
    {
        if (_isApplyingStoredSettings)
        {
            return;
        }

        _ = _logcatSettings.SetSettingsAsync(new LogcatSettings(
            new LogcatSearchTerms(IncludeTerms.ToArray(), ExcludeTerms.ToArray()),
            BuildFilterSettings()));
    }

    private void OnStoredSettingsChanged(object? sender, EventArgs e)
    {
        ApplyStoredSettings();
        RebuildVisibleLines();
    }

    private void ApplyStoredSettings()
    {
        var settings = _logcatSettings.Settings;

        // Слепок обновляем всегда: на старте подписка на коллекции ещё не стоит,
        // и без этого сохранённые условия не применились бы до первой правки.
        _activeTerms = settings.Search;

        _isApplyingStoredSettings = true;
        try
        {
            ReplaceAll(IncludeTerms, settings.Search.Include);
            ReplaceAll(ExcludeTerms, settings.Search.Exclude);
            ReplaceAll(FilterTags, settings.Filter.Tags);

            SelectedLevel = LogcatLevelOptionViewModel.All
                .FirstOrDefault(option => option.Level == settings.Filter.Level)
                ?? LogcatLevelOptionViewModel.All[0];

            OnlyNetarium = settings.Filter.OnlyNetarium;
            ExtraArguments = settings.Filter.ExtraArguments;
        }
        finally
        {
            _isApplyingStoredSettings = false;
        }
    }

    private static void ReplaceAll(ObservableCollection<string> target, IReadOnlyList<string> values)
    {
        if (target.SequenceEqual(values, StringComparer.Ordinal))
        {
            return;
        }

        target.Clear();
        foreach (var value in values)
        {
            target.Add(value);
        }
    }

    private void ClearLines()
    {
        while (_pendingLines.TryDequeue(out _))
        {
        }

        _allLines.Clear();
        VisibleLines.Clear();
        SelectedLines.Clear();
        OnPropertyChanged(nameof(LineSummary));
        OnPropertyChanged(nameof(HasVisibleLines));
        OnPropertyChanged(nameof(EmptyStateMessage));
        NotifyCommandStateChanged();
    }

    private void CopySelectedLines()
    {
        var lines = SelectedLines.Count > 0 ? SelectedLines : VisibleLines;
        if (lines.Count == 0)
        {
            return;
        }

        try
        {
            Clipboard.SetText(string.Join(Environment.NewLine, lines.Select(item => item.Text)));
            StatusText = $"Скопировано строк: {lines.Count}";
        }
        catch (Exception ex)
        {
            StatusText = $"Не удалось скопировать: {ex.Message}";
        }
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
        CopySelectedCommand.NotifyCanExecuteChanged();
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
        _filterRestartTimer.Stop();
        _filterRestartTimer.Tick -= OnFilterRestartTimerTick;
        _processIdTimer.Stop();
        _processIdTimer.Tick -= OnProcessIdTimerTick;
        _deviceInventory.KnownDevices.CollectionChanged -= OnKnownDevicesChanged;
        _deviceAliases.Changed -= OnAliasesChanged;
        _logcatSettings.Changed -= OnStoredSettingsChanged;
        IncludeTerms.CollectionChanged -= OnSearchTermsChanged;
        ExcludeTerms.CollectionChanged -= OnSearchTermsChanged;
        FilterTags.CollectionChanged -= OnFilterTagsChanged;
        _ = StopAsync();
    }
}

public sealed record LogcatDeviceOptionViewModel(TvDeviceProfile Device, string TargetId, string DisplayText)
{
    public override string ToString() => DisplayText;
}

/// <summary>
/// Строка лога сравнивается по ссылке, а не по содержимому. Одинаковых строк в логе
/// сколько угодно, а выделение в WPF ищет элемент равенством: с record выделение одной
/// строки прилипало бы к первому её двойнику, и рамка работала бы вразнобой.
/// </summary>
public sealed class LogcatLineItemViewModel(string text, bool isError)
{
    public string Text { get; } = text;

    public bool IsError { get; } = isError;
}

/// <summary>
/// Уровень — порог: выбранный и всё, что важнее. Свойство <see cref="DisplayText"/> —
/// общая для приложения конвенция отображения в выпадающих списках.
/// </summary>
public sealed record LogcatLevelOptionViewModel(LogcatLevel Level, string DisplayText)
{
    public static IReadOnlyList<LogcatLevelOptionViewModel> All { get; } =
    [
        new(LogcatLevel.All, "Всё"),
        new(LogcatLevel.Debug, "Debug и выше"),
        new(LogcatLevel.Info, "Info и выше"),
        new(LogcatLevel.Warn, "Warn и выше"),
        new(LogcatLevel.Error, "Только ошибки")
    ];

    public override string ToString() => DisplayText;
}
