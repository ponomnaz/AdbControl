using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Globalization;
using AdbControl.Application.Apk;
using AdbControl.Application.Common;

namespace AdbControl.Tools.Apk.ViewModels;

public sealed class ApkLibraryToolViewModel : ObservableObject
{
    private readonly IApkLibraryService _apkLibrary;
    private readonly List<ApkLibraryEntry> _allEntries = [];
    private bool _isBusy;
    private bool _isDropActive;
    private string _statusText = "Перетащи APK сюда или загрузи файл.";
    private ApkSortField _sortField = ApkSortField.Name;
    private bool _isSortDescending;
    private int _selectedTargetDeviceCount;

    public ApkLibraryToolViewModel(IApkLibraryService apkLibrary)
    {
        _apkLibrary = apkLibrary;

        DeleteSelectedCommand = new RelayCommand(
            () => _ = DeleteSelectedAsync(),
            () => CanDeleteSelected());
        InstallSelectedCommand = new RelayCommand(
            () => { },
            () => CanInstallSelected());
        SortByNameCommand = new RelayCommand(() => SetSortField(ApkSortField.Name));
        SortByDateCommand = new RelayCommand(() => SetSortField(ApkSortField.Date));
        SortBySizeCommand = new RelayCommand(() => SetSortField(ApkSortField.Size));
        ToggleSortDirectionCommand = new RelayCommand(ToggleSortDirection);

        SelectedEntries.CollectionChanged += OnSelectedEntriesChanged;

        _ = LoadAsync();
    }

    public ObservableCollection<ApkLibraryItemViewModel> Entries { get; } = [];

    public ObservableCollection<ApkLibraryItemViewModel> SelectedEntries { get; } = [];

    public RelayCommand DeleteSelectedCommand { get; }
    public RelayCommand InstallSelectedCommand { get; }
    public RelayCommand SortByNameCommand { get; }
    public RelayCommand SortByDateCommand { get; }
    public RelayCommand SortBySizeCommand { get; }
    public RelayCommand ToggleSortDirectionCommand { get; }

    public string StatusText
    {
        get => _statusText;
        private set
        {
            if (SetProperty(ref _statusText, value))
            {
                OnPropertyChanged(nameof(HasStatusText));
            }
        }
    }

    public bool HasStatusText => !string.IsNullOrWhiteSpace(StatusText);

    public bool IsDropActive
    {
        get => _isDropActive;
        set => SetProperty(ref _isDropActive, value);
    }

    public bool HasEntries => Entries.Count > 0;

    public string LibrarySummary => $"APK: {Entries.Count}";

    public string SelectionSummary => SelectedEntries.Count == 0
        ? "Ничего не выбрано"
        : SelectedEntries.Count == 1
            ? "Выбран 1 APK"
            : $"Выбрано: {SelectedEntries.Count}";

    public int SelectedTargetDeviceCount
    {
        get => _selectedTargetDeviceCount;
        set
        {
            if (SetProperty(ref _selectedTargetDeviceCount, value))
            {
                OnPropertyChanged(nameof(HasSelectedTargets));
                NotifyCommandStateChanged();
            }
        }
    }

    public bool HasSelectedTargets => SelectedTargetDeviceCount > 0;

    public string EmptyStateMessage => "Пока нет загруженных APK.";

    public bool IsSortByName
    {
        get => _sortField == ApkSortField.Name;
        set
        {
            if (value)
            {
                SetSortField(ApkSortField.Name);
            }
        }
    }

    public bool IsSortByDate
    {
        get => _sortField == ApkSortField.Date;
        set
        {
            if (value)
            {
                SetSortField(ApkSortField.Date);
            }
        }
    }

    public bool IsSortBySize
    {
        get => _sortField == ApkSortField.Size;
        set
        {
            if (value)
            {
                SetSortField(ApkSortField.Size);
            }
        }
    }

    public string SortDirectionGlyph => _isSortDescending ? "↓" : "↑";

    public async Task ImportFilesAsync(IReadOnlyList<string> filePaths)
    {
        if (filePaths.Count == 0)
        {
            return;
        }

        try
        {
            _isBusy = true;
            NotifyCommandStateChanged();
            StatusText = filePaths.Count == 1 ? "Загрузка APK..." : $"Загрузка APK: {filePaths.Count}";

            var result = await _apkLibrary.ImportAsync(filePaths);
            await ReloadEntriesAsync();

            var addedCount = result.ImportedEntries.Count;
            var skippedCount = result.SkippedPaths.Count;

            StatusText = skippedCount == 0
                ? $"Добавлено: {addedCount}"
                : $"Добавлено: {addedCount}, пропущено: {skippedCount}";
        }
        catch (Exception ex)
        {
            StatusText = ex.Message;
        }
        finally
        {
            _isBusy = false;
            IsDropActive = false;
            NotifyCommandStateChanged();
        }
    }

    private async Task LoadAsync()
    {
        try
        {
            _isBusy = true;
            NotifyCommandStateChanged();
            await ReloadEntriesAsync();
            StatusText = Entries.Count == 0
                ? "Перетащи APK сюда или загрузи файл."
                : string.Empty;
        }
        catch (Exception ex)
        {
            StatusText = ex.Message;
        }
        finally
        {
            _isBusy = false;
            NotifyCommandStateChanged();
        }
    }

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
            _isBusy = true;
            NotifyCommandStateChanged();
            StatusText = selectedIds.Length == 1 ? "Удаление APK..." : $"Удаление APK: {selectedIds.Length}";

            var removedCount = await _apkLibrary.DeleteAsync(selectedIds);
            SelectedEntries.Clear();
            await ReloadEntriesAsync();

            StatusText = removedCount == 0
                ? "Удалять нечего"
                : removedCount == 1
                    ? "APK удален"
                    : $"APK удалено: {removedCount}";
        }
        catch (Exception ex)
        {
            StatusText = ex.Message;
        }
        finally
        {
            _isBusy = false;
            NotifyCommandStateChanged();
        }
    }

    private async Task ReloadEntriesAsync()
    {
        var entries = await _apkLibrary.GetEntriesAsync();
        _allEntries.Clear();
        _allEntries.AddRange(entries);
        RebuildEntries();
    }

    private void RebuildEntries()
    {
        var originalOrder = _allEntries
            .Select((entry, index) => new OrderedApkEntry(entry, index))
            .ToArray();

        var duplicateSuffixes = BuildDuplicateSuffixMap(originalOrder);
        var orderedEntries = SortEntries(originalOrder);

        SelectedEntries.Clear();
        Entries.Clear();

        foreach (var entry in orderedEntries)
        {
            duplicateSuffixes.TryGetValue(entry.Entry.Id, out var duplicateSuffix);
            Entries.Add(new ApkLibraryItemViewModel(entry.Entry, duplicateSuffix));
        }

        OnPropertyChanged(nameof(LibrarySummary));
        OnPropertyChanged(nameof(SelectionSummary));
        OnPropertyChanged(nameof(EmptyStateMessage));
        OnPropertyChanged(nameof(HasEntries));
    }

    private IReadOnlyList<OrderedApkEntry> SortEntries(IReadOnlyList<OrderedApkEntry> entries)
    {
        Func<OrderedApkEntry, string> nameSelector = static entry => entry.Entry.DisplayName;
        Func<OrderedApkEntry, DateTimeOffset> dateSelector = static entry => entry.Entry.ImportedAt;
        Func<OrderedApkEntry, long> sizeSelector = static entry => entry.Entry.FileSizeBytes;

        return (_sortField, _isSortDescending) switch
        {
            (ApkSortField.Name, false) => entries
                .OrderBy(nameSelector, StringComparer.CurrentCultureIgnoreCase)
                .ThenBy(dateSelector)
                .ThenBy(static entry => entry.OriginalIndex)
                .ToArray(),
            (ApkSortField.Name, true) => entries
                .OrderByDescending(nameSelector, StringComparer.CurrentCultureIgnoreCase)
                .ThenByDescending(dateSelector)
                .ThenBy(static entry => entry.OriginalIndex)
                .ToArray(),
            (ApkSortField.Date, false) => entries
                .OrderBy(dateSelector)
                .ThenBy(nameSelector, StringComparer.CurrentCultureIgnoreCase)
                .ThenBy(static entry => entry.OriginalIndex)
                .ToArray(),
            (ApkSortField.Date, true) => entries
                .OrderByDescending(dateSelector)
                .ThenBy(nameSelector, StringComparer.CurrentCultureIgnoreCase)
                .ThenBy(static entry => entry.OriginalIndex)
                .ToArray(),
            (ApkSortField.Size, false) => entries
                .OrderBy(sizeSelector)
                .ThenBy(nameSelector, StringComparer.CurrentCultureIgnoreCase)
                .ThenByDescending(dateSelector)
                .ThenBy(static entry => entry.OriginalIndex)
                .ToArray(),
            _ => entries
                .OrderByDescending(sizeSelector)
                .ThenBy(nameSelector, StringComparer.CurrentCultureIgnoreCase)
                .ThenByDescending(dateSelector)
                .ThenBy(static entry => entry.OriginalIndex)
                .ToArray()
        };
    }

    private bool CanDeleteSelected()
    {
        return !_isBusy && SelectedEntries.Count > 0;
    }

    private bool CanInstallSelected()
    {
        return !_isBusy && SelectedEntries.Count > 0 && SelectedTargetDeviceCount > 0;
    }

    private void OnSelectedEntriesChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        OnPropertyChanged(nameof(SelectionSummary));
        NotifyCommandStateChanged();
    }

    private void NotifyCommandStateChanged()
    {
        DeleteSelectedCommand.NotifyCanExecuteChanged();
        InstallSelectedCommand.NotifyCanExecuteChanged();
    }

    private void SetSortField(ApkSortField field)
    {
        if (_sortField == field)
        {
            return;
        }

        _sortField = field;
        OnPropertyChanged(nameof(IsSortByName));
        OnPropertyChanged(nameof(IsSortByDate));
        OnPropertyChanged(nameof(IsSortBySize));
        RebuildEntries();
    }

    private void ToggleSortDirection()
    {
        _isSortDescending = !_isSortDescending;
        OnPropertyChanged(nameof(SortDirectionGlyph));
        RebuildEntries();
    }

    private static Dictionary<Guid, int> BuildDuplicateSuffixMap(IReadOnlyList<OrderedApkEntry> entries)
    {
        var suffixes = new Dictionary<Guid, int>();

        foreach (var group in entries.GroupBy(static entry => new DuplicateKey(
                     entry.Entry.DisplayName,
                     entry.Entry.ImportedAt.ToLocalTime().ToString("dd.MM.yyyy HH:mm", CultureInfo.InvariantCulture))))
        {
            if (group.Count() <= 1)
            {
                continue;
            }

            var order = 1;
            foreach (var entry in group.OrderBy(static item => item.OriginalIndex))
            {
                if (order > 1)
                {
                    suffixes[entry.Entry.Id] = order;
                }

                order++;
            }
        }

        return suffixes;
    }

    private sealed record OrderedApkEntry(ApkLibraryEntry Entry, int OriginalIndex);
    private sealed record DuplicateKey(string DisplayName, string ImportedAtKey);
}

public sealed class ApkLibraryItemViewModel : ObservableObject
{
    public ApkLibraryItemViewModel(ApkLibraryEntry entry, int duplicateSuffix)
    {
        Id = entry.Id;
        DisplayName = duplicateSuffix > 0 ? $"{entry.DisplayName} {duplicateSuffix}" : entry.DisplayName;
        FileSizeBytes = entry.FileSizeBytes;
        ImportedAt = entry.ImportedAt;
    }

    public Guid Id { get; }

    public string DisplayName { get; }

    public long FileSizeBytes { get; }

    public DateTimeOffset ImportedAt { get; }

    public string MetaText => $"{FormatSize(FileSizeBytes)} • {ImportedAt:dd.MM.yyyy HH:mm}";

    private static string FormatSize(long bytes)
    {
        const double kilo = 1024d;
        const double mega = kilo * 1024d;
        const double giga = mega * 1024d;

        if (bytes >= giga)
        {
            return $"{bytes / giga:0.##} GB";
        }

        if (bytes >= mega)
        {
            return $"{bytes / mega:0.##} MB";
        }

        if (bytes >= kilo)
        {
            return $"{bytes / kilo:0.##} KB";
        }

        return $"{bytes} B";
    }
}

public enum ApkSortField
{
    Name,
    Date,
    Size
}
