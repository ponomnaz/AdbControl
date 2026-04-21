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
        SelectedEntries.CollectionChanged += OnSelectedEntriesChanged;

        DeleteSelectedCommand = new RelayCommand(
            () => _ = DeleteSelectedAsync(),
            () => CanDeleteSelected());

        ClearAllCommand = new RelayCommand(
            () => _ = ClearAllAsync(),
            () => CanClearAll());

        RebuildDeviceFilters();
        RebuildVisibleEntries();
    }

    public ObservableCollection<CommandLogDeviceFilterItem> DeviceFilters { get; } = [];

    public ObservableCollection<CommandTraceEntry> VisibleEntries { get; } = [];

    public ObservableCollection<CommandTraceEntry> SelectedEntries { get; } = [];

    public RelayCommand DeleteSelectedCommand { get; }

    public RelayCommand ClearAllCommand { get; }

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

            SelectedEntries.Clear();
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

    public string SelectedStdout => string.IsNullOrWhiteSpace(SelectedEntry?.Stdout)
        ? "Пусто"
        : SelectedEntry.Stdout;

    public string SelectedStderr => string.IsNullOrWhiteSpace(SelectedEntry?.Stderr)
        ? "Пусто"
        : SelectedEntry.Stderr;

    private async Task DeleteSelectedAsync()
    {
        var selectedIds = SelectedEntries
            .Select(entry => entry.Id)
            .Distinct()
            .ToArray();

        if (selectedIds.Length == 0)
        {
            return;
        }

        try
        {
            _isMutating = true;
            NotifyCommandStateChanged();

            SelectedEntry = null;
            await _commandTraceJournal.RemoveEntriesAsync(selectedIds);
            SelectedEntries.Clear();
        }
        finally
        {
            _isMutating = false;
            NotifyCommandStateChanged();
        }
    }

    private async Task ClearAllAsync()
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
            SelectedEntries.Clear();
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

    private void OnSelectedEntriesChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        SelectedEntry = SelectedEntries.LastOrDefault();

        NotifyCommandStateChanged();
    }

    private bool CanDeleteSelected()
    {
        return !_isMutating && SelectedEntries.Count > 0;
    }

    private bool CanClearAll()
    {
        return !_isMutating && VisibleEntries.Count > 0;
    }

    private void NotifyCommandStateChanged()
    {
        DeleteSelectedCommand.NotifyCanExecuteChanged();
        ClearAllCommand.NotifyCanExecuteChanged();
    }

    private void RebuildDeviceFilters()
    {
        var previouslySelectedKey = SelectedDeviceFilter?.Key ?? CommandLogDeviceFilterItem.AllKey;

        var endpointItems = _commandTraceJournal.Entries
            .Select(entry => ExtractEndpoint(entry.CommandText))
            .Where(static endpoint => !string.IsNullOrWhiteSpace(endpoint))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(endpoint => CommandLogDeviceFilterItem.CreateDevice(endpoint!, BuildFilterDisplayText(endpoint!)))
            .OrderBy(static item => item.DisplayText, StringComparer.CurrentCultureIgnoreCase)
            .ToArray();

        DeviceFilters.Clear();
        DeviceFilters.Add(_allDevicesFilter);

        foreach (var item in endpointItems)
        {
            DeviceFilters.Add(item);
        }

        var nextSelectedItem = DeviceFilters.FirstOrDefault(item =>
            string.Equals(item.Key, previouslySelectedKey, StringComparison.Ordinal))
            ?? _allDevicesFilter;

        if (!ReferenceEquals(SelectedDeviceFilter, nextSelectedItem))
        {
            SelectedDeviceFilter = nextSelectedItem;
            return;
        }

        if (!DeviceFilters.Any(item =>
                string.Equals(item.Key, previouslySelectedKey, StringComparison.Ordinal)))
        {
            SelectedDeviceFilter = _allDevicesFilter;
        }
    }

    private void RebuildVisibleEntries()
    {
        var selectedEndpoint = SelectedDeviceFilter?.Endpoint;

        var visibleEntries = _commandTraceJournal.Entries
            .Where(entry => selectedEndpoint is null ||
                            string.Equals(ExtractEndpoint(entry.CommandText), selectedEndpoint, StringComparison.OrdinalIgnoreCase))
            .ToArray();

        VisibleEntries.Clear();
        foreach (var entry in visibleEntries)
        {
            VisibleEntries.Add(entry);
        }
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

public sealed record CommandLogDeviceFilterItem(string Key, string? Endpoint, string DisplayText)
{
    public const string AllKey = "__all__";

    public bool IsAll => Endpoint is null;

    public static CommandLogDeviceFilterItem CreateAll() => new(AllKey, null, "Все телевизоры");

    public static CommandLogDeviceFilterItem CreateDevice(string endpoint, string displayText) => new(endpoint, endpoint, displayText);

    public override string ToString() => DisplayText;
}
