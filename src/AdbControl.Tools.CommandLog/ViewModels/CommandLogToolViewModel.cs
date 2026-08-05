using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Text.RegularExpressions;
using AdbControl.Application.Common;
using AdbControl.Application.Devices;
using AdbControl.Application.Diagnostics;

namespace AdbControl.Tools.CommandLog.ViewModels;

public sealed class CommandLogToolViewModel : ObservableObject
{
    private static readonly Regex DeviceTargetRegex = new(
        @"(?:-s|connect|disconnect)\s+(?<endpoint>(?:\d{1,3}\.){3}\d{1,3}(?::\d{1,5})?)",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private readonly CommandTraceJournal _commandTraceJournal;
    private readonly DeviceAliasCatalog _deviceAliases;
    private readonly CommandLogDeviceFilterItem _allDevicesFilter = CommandLogDeviceFilterItem.CreateAll();
    private CommandTraceEntry? _selectedEntry;
    private bool _isMutating;
    private CommandLogDeviceFilterItem? _selectedDeviceFilter;

    public CommandLogToolViewModel(CommandTraceJournal commandTraceJournal, DeviceAliasCatalog deviceAliases)
    {
        _commandTraceJournal = commandTraceJournal;
        _deviceAliases = deviceAliases;
        _commandTraceJournal.Entries.CollectionChanged += OnEntriesChanged;
        _deviceAliases.Changed += OnAliasesChanged;

        DeleteSelectedCommand = new RelayCommand(
            () => _ = DeleteSelectedAsync(),
            () => CanDeleteSelected());

        ClearVisibleCommand = new RelayCommand(
            () => _ = ClearVisibleAsync(),
            () => CanClearVisible());

        RebuildDeviceFilters();
        RebuildVisibleEntries();
    }

    public ObservableCollection<CommandLogDeviceFilterItem> DeviceFilters { get; } = [];

    public ObservableCollection<CommandTraceEntry> VisibleEntries { get; } = [];

    public RelayCommand DeleteSelectedCommand { get; }

    public RelayCommand ClearVisibleCommand { get; }

    public CommandLogDeviceFilterItem? SelectedDeviceFilter
    {
        get => _selectedDeviceFilter;
        set
        {
            var nextValue = value ?? _allDevicesFilter;

            if (!SetProperty(ref _selectedDeviceFilter, nextValue))
            {
                return;
            }

            SelectedEntry = null;
            RebuildVisibleEntries();
            OnPropertyChanged(nameof(TotalSummary));
            OnPropertyChanged(nameof(ErrorSummary));
            OnPropertyChanged(nameof(EmptyStateMessage));
            NotifyCommandStateChanged();
        }
    }

    public CommandTraceEntry? SelectedEntry
    {
        get => _selectedEntry;
        set
        {
            if (!SetProperty(ref _selectedEntry, value))
            {
                return;
            }

            OnPropertyChanged(nameof(HasSelection));
            OnPropertyChanged(nameof(SelectedTimestamp));
            OnPropertyChanged(nameof(SelectedCommandText));
            OnPropertyChanged(nameof(SelectedExitCode));
            OnPropertyChanged(nameof(SelectedStdout));
            OnPropertyChanged(nameof(SelectedStderr));
            OnPropertyChanged(nameof(SelectedDuration));
            NotifyCommandStateChanged();
        }
    }

    public bool HasSelection => SelectedEntry is not null;

    public string TotalSummary => $"Команд: {VisibleEntries.Count}";

    public string ErrorSummary => $"Ошибок: {VisibleEntries.Count(static x => x.IsError)}";

    public string EmptyStateMessage => GetVisibleEntriesCount() == 0
        ? SelectedDeviceFilter?.IsAll != false
            ? "Команды пока не выполнялись."
            : "Для выбранного телевизора команд пока нет."
        : "Выбери запись, чтобы посмотреть вывод.";

    public string SelectedTimestamp => SelectedEntry?.Timestamp.ToString("dd.MM.yyyy HH:mm:ss") ?? string.Empty;

    public string SelectedCommandText => SelectedEntry?.CommandText ?? string.Empty;

    public string SelectedExitCode => SelectedEntry is null
        ? string.Empty
        : $"Код выхода: {SelectedEntry.ExitCode}";

    public string SelectedDuration => SelectedEntry?.DurationMs is { } duration
        ? $"Заняла: {FormatDuration(duration)}"
        : string.Empty;

    /// <summary>У записей старше появления поля длительности нет — ставим прочерк.</summary>
    public static string FormatDuration(int? durationMs)
    {
        return durationMs is not { } value
            ? "—"
            : value >= 1000
                ? $"{value / 1000d:0.0} с"
                : $"{value} мс";
    }

    public string SelectedStdout => string.IsNullOrWhiteSpace(SelectedEntry?.Stdout)
        ? "Пусто"
        : SelectedEntry.Stdout;

    public string SelectedStderr => string.IsNullOrWhiteSpace(SelectedEntry?.Stderr)
        ? "Пусто"
        : SelectedEntry.Stderr;

    private async Task DeleteSelectedAsync()
    {
        if (SelectedEntry is not { } entry)
        {
            return;
        }

        try
        {
            _isMutating = true;
            NotifyCommandStateChanged();

            SelectedEntry = null;
            await _commandTraceJournal.RemoveEntriesAsync([entry.Id]);
        }
        finally
        {
            _isMutating = false;
            NotifyCommandStateChanged();
        }
    }

    /// <summary>Стирает то, что показано: при выбранном фильтре — только его записи.</summary>
    private async Task ClearVisibleAsync()
    {
        var visibleEntryIds = VisibleEntries
            .Select(entry => entry.Id)
            .ToArray();

        if (visibleEntryIds.Length == 0)
        {
            return;
        }

        try
        {
            _isMutating = true;
            NotifyCommandStateChanged();

            SelectedEntry = null;
            await _commandTraceJournal.RemoveEntriesAsync(visibleEntryIds);
        }
        finally
        {
            _isMutating = false;
            NotifyCommandStateChanged();
        }
    }

    private void OnEntriesChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        RebuildDeviceFilters();
        RebuildVisibleEntries();

        if (SelectedEntry is not null && VisibleEntries.All(entry => entry.Id != SelectedEntry.Id))
        {
            SelectedEntry = null;
        }

        if (GetVisibleEntriesCount() == 0)
        {
            SelectedEntry = null;
        }

        OnPropertyChanged(nameof(TotalSummary));
        OnPropertyChanged(nameof(ErrorSummary));
        OnPropertyChanged(nameof(EmptyStateMessage));
        NotifyCommandStateChanged();
    }

    private void OnAliasesChanged(object? sender, EventArgs e)
    {
        RebuildDeviceFilters();
        RebuildVisibleEntries();
        OnPropertyChanged(nameof(EmptyStateMessage));
    }

    private bool CanDeleteSelected()
    {
        return !_isMutating && SelectedEntry is not null;
    }

    private bool CanClearVisible()
    {
        return !_isMutating && VisibleEntries.Count > 0;
    }

    private void NotifyCommandStateChanged()
    {
        DeleteSelectedCommand.NotifyCanExecuteChanged();
        ClearVisibleCommand.NotifyCanExecuteChanged();
    }

    /// <summary>
    /// Сводит список фильтров к нужному, не пересоздавая уцелевшие пункты: пересборка
    /// закрывала раскрытый список под курсором и сбрасывала выбор при каждой команде.
    /// </summary>
    private void RebuildDeviceFilters()
    {
        var endpoints = _commandTraceJournal.Entries
            .Select(entry => ExtractEndpoint(entry.CommandText))
            .Where(static endpoint => !string.IsNullOrWhiteSpace(endpoint))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(endpoint => endpoint!)
            .OrderBy(BuildFilterDisplayText, StringComparer.CurrentCultureIgnoreCase)
            .ToArray();

        var index = 1;

        if (DeviceFilters.Count == 0)
        {
            DeviceFilters.Add(_allDevicesFilter);
        }

        foreach (var endpoint in endpoints)
        {
            var existing = DeviceFilters.FirstOrDefault(item =>
                string.Equals(item.Key, endpoint, StringComparison.OrdinalIgnoreCase));

            if (existing is not null)
            {
                // Псевдоним телевизора мог поменяться — правим подпись, а не объект.
                existing.DisplayText = BuildFilterDisplayText(endpoint);

                var current = DeviceFilters.IndexOf(existing);
                if (current != index)
                {
                    DeviceFilters.Move(current, index);
                }
            }
            else
            {
                DeviceFilters.Insert(index, CommandLogDeviceFilterItem.CreateDevice(endpoint, BuildFilterDisplayText(endpoint)));
            }

            index++;
        }

        while (DeviceFilters.Count > index)
        {
            var dropped = DeviceFilters[^1];
            DeviceFilters.RemoveAt(DeviceFilters.Count - 1);

            // Выбранный телевизор пропал из журнала — возвращаемся ко всем.
            if (ReferenceEquals(SelectedDeviceFilter, dropped))
            {
                SelectedDeviceFilter = _allDevicesFilter;
            }
        }

        SelectedDeviceFilter ??= _allDevicesFilter;
    }

    /// <summary>
    /// Сводит видимые записи к отобранным. Пересборка сбрасывала выделение и прокрутку,
    /// а журнал пополняется постоянно — читать выбранную запись было невозможно.
    /// </summary>
    private void RebuildVisibleEntries()
    {
        var selectedEndpoint = SelectedDeviceFilter?.Endpoint;

        var desired = _commandTraceJournal.Entries
            .Where(entry => selectedEndpoint is null ||
                            string.Equals(ExtractEndpoint(entry.CommandText), selectedEndpoint, StringComparison.OrdinalIgnoreCase))
            .ToArray();

        for (var index = 0; index < desired.Length; index++)
        {
            if (index < VisibleEntries.Count && ReferenceEquals(VisibleEntries[index], desired[index]))
            {
                continue;
            }

            var current = IndexOfSame(desired[index], index);

            if (current >= 0)
            {
                VisibleEntries.Move(current, index);
            }
            else
            {
                VisibleEntries.Insert(index, desired[index]);
            }
        }

        while (VisibleEntries.Count > desired.Length)
        {
            VisibleEntries.RemoveAt(VisibleEntries.Count - 1);
        }
    }

    private int IndexOfSame(CommandTraceEntry entry, int from)
    {
        for (var index = from; index < VisibleEntries.Count; index++)
        {
            if (ReferenceEquals(VisibleEntries[index], entry))
            {
                return index;
            }
        }

        return -1;
    }

    private string BuildFilterDisplayText(string endpoint)
    {
        var alias = _deviceAliases.GetAlias(endpoint);
        return string.IsNullOrWhiteSpace(alias)
            ? endpoint
            : $"{alias} ({endpoint})";
    }

    private int GetVisibleEntriesCount() => VisibleEntries.Count;

    private static string? ExtractEndpoint(string commandText)
    {
        if (string.IsNullOrWhiteSpace(commandText))
        {
            return null;
        }

        var match = DeviceTargetRegex.Match(commandText);
        if (!match.Success)
        {
            return null;
        }

        var endpoint = match.Groups["endpoint"].Value.Trim();
        return string.IsNullOrWhiteSpace(endpoint)
            ? null
            : endpoint.Contains(':', StringComparison.Ordinal)
                ? endpoint
                : $"{endpoint}:5555";
    }
}

/// <summary>
/// Пункт фильтра. Не запись: подпись меняется при переименовании телевизора, а сам
/// объект обязан остаться тем же — иначе выпадающий список теряет выбранное.
/// </summary>
public sealed class CommandLogDeviceFilterItem : ObservableObject
{
    public const string AllKey = "__all__";

    private string _displayText;

    private CommandLogDeviceFilterItem(string key, string? endpoint, string displayText)
    {
        Key = key;
        Endpoint = endpoint;
        _displayText = displayText;
    }

    public string Key { get; }

    public string? Endpoint { get; }

    public string DisplayText
    {
        get => _displayText;
        set => SetProperty(ref _displayText, value);
    }

    public bool IsAll => Endpoint is null;

    public static CommandLogDeviceFilterItem CreateAll() => new(AllKey, null, "Все телевизоры");

    public static CommandLogDeviceFilterItem CreateDevice(string endpoint, string displayText) => new(endpoint, endpoint, displayText);

    public override string ToString() => DisplayText;
}
