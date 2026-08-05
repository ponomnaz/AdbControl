using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using AdbControl.Application.Common;
using AdbControl.Application.Files;

namespace AdbControl.Tools.Gallery.ViewModels;

/// <summary>
/// Болванка вкладки: список и превью читают настоящую папку снимков, но ничего в ней
/// не меняют. Действия, которые правят диск, пока только сообщают о себе в статусе.
/// </summary>
public sealed class ScreenshotGalleryToolViewModel : ObservableObject, IDisposable
{
    private static readonly string[] ImageExtensions = [".png", ".jpg", ".jpeg", ".bmp", ".webp"];

    /// <summary>
    /// Снимок пишется на диск не одним событием: сначала «создан», потом несколько
    /// «изменён» по мере роста файла. Ждём тишины, иначе список дёргается на каждое.
    /// </summary>
    private static readonly TimeSpan SyncDelay = TimeSpan.FromMilliseconds(400);

    private static readonly TimeSpan SaveDelay = TimeSpan.FromMilliseconds(800);

    private readonly IScreenshotLibraryService _library;
    private readonly IGallerySettingsStore _settingsStore;
    private readonly string _rootDirectory;
    private readonly Dispatcher _dispatcher;
    private readonly DispatcherTimer _syncTimer;
    private readonly DispatcherTimer _saveTimer;
    private FileSystemWatcher? _watcher;
    private GridLength _listWidth = new(300);
    private string _currentDirectory;
    private GalleryEntryViewModel? _selectedEntry;
    private BitmapImage? _preview;
    private string _statusText = string.Empty;
    private string _pixelSizeText = string.Empty;
    private GallerySortKey _sortKey = GallerySortKey.Taken;
    private bool _sortDescending = true;

    public ScreenshotGalleryToolViewModel(IScreenshotLibraryService library, IGallerySettingsStore settingsStore)
    {
        _library = library;
        _settingsStore = settingsStore;
        _rootDirectory = library.RootDirectory;
        _currentDirectory = _rootDirectory;

        RefreshCommand = new RelayCommand(Refresh);
        NavigateCommand = new RelayCommand<GalleryCrumbViewModel>(crumb =>
        {
            if (crumb is not null)
            {
                Navigate(crumb.FullPath);
            }
        });
        SortByCommand = new RelayCommand<GallerySortKey>(SortBy);
        ToggleSortDirectionCommand = new RelayCommand(() => SetSort(_sortKey, !_sortDescending));
        OpenCommand = new RelayCommand(ActivateSelected, () => SelectedEntry is not null);
        RevealCommand = new RelayCommand(RevealSelected, () => SelectedEntry is not null);
        NewFolderCommand = new RelayCommand(CreateFolder);
        RenameCommand = new RelayCommand(BeginRename, () => SelectedEntry is not null);
        MoveToCommand = new RelayCommand<GalleryMoveTargetViewModel>(MoveTo);
        DeleteCommand = new RelayCommand(Delete, () => SelectedEntries.Count > 0);

        SelectedEntries.CollectionChanged += OnSelectedEntriesChanged;

        _dispatcher = Dispatcher.CurrentDispatcher;
        _syncTimer = new DispatcherTimer(SyncDelay, DispatcherPriority.Background, OnSyncTick, _dispatcher);
        _syncTimer.Stop();

        _saveTimer = new DispatcherTimer(SaveDelay, DispatcherPriority.Background, OnSaveTick, _dispatcher);
        _saveTimer.Stop();

        Refresh();
        StartWatching();

        _ = LoadSettingsAsync();
    }

    private async Task LoadSettingsAsync()
    {
        var settings = await _settingsStore.ReadAsync() ?? GallerySettings.Default;

        _sortKey = settings.SortKey switch
        {
            "name" => GallerySortKey.Name,
            "size" => GallerySortKey.Size,
            _ => GallerySortKey.Taken
        };

        _sortDescending = settings.SortDescending;
        _listWidth = new GridLength(Math.Clamp(settings.ListWidth, 200, 900));

        OnPropertyChanged(nameof(ListWidth));
        SetSort(_sortKey, _sortDescending);
    }

    private void SaveSettingsSoon()
    {
        // Ширину тянут мышью — сохранять на каждый пиксель незачем.
        _saveTimer.Stop();
        _saveTimer.Start();
    }

    private void OnSaveTick(object? sender, EventArgs e)
    {
        _saveTimer.Stop();

        var key = _sortKey switch
        {
            GallerySortKey.Name => "name",
            GallerySortKey.Size => "size",
            _ => "taken"
        };

        _ = _settingsStore.WriteAsync(new GallerySettings(key, _sortDescending, _listWidth.Value));
    }

    public ObservableCollection<GalleryEntryViewModel> Entries { get; } = [];

    public ObservableCollection<GalleryEntryViewModel> SelectedEntries { get; } = [];

    public ObservableCollection<GalleryCrumbViewModel> Crumbs { get; } = [];

    public RelayCommand RefreshCommand { get; }

    public RelayCommand<GalleryCrumbViewModel> NavigateCommand { get; }

    public RelayCommand<GallerySortKey> SortByCommand { get; }

    public RelayCommand ToggleSortDirectionCommand { get; }

    public RelayCommand OpenCommand { get; }

    public RelayCommand RevealCommand { get; }

    public RelayCommand NewFolderCommand { get; }

    public RelayCommand RenameCommand { get; }

    public RelayCommand<GalleryMoveTargetViewModel> MoveToCommand { get; }

    /// <summary>Куда можно перенести выделенное: подпапки текущей и уровень выше.</summary>
    public ObservableCollection<GalleryMoveTargetViewModel> MoveTargets { get; } = [];

    public bool HasMoveTargets => MoveTargets.Count > 0;

    public bool HasEntries => Entries.Count > 0;

    /// <summary>Ширина списка: её тянут разделителем, и она запоминается между запусками.</summary>
    public GridLength ListWidth
    {
        get => _listWidth;
        set
        {
            if (SetProperty(ref _listWidth, value))
            {
                SaveSettingsSoon();
            }
        }
    }

    public string EmptyStateMessage => IsAtRoot
        ? "Снимков пока нет. Сделай снимок экрана во вкладке «Устройства» — он появится здесь сам."
        : "Папка пуста. Перенеси сюда снимки через меню правой кнопки.";

    public RelayCommand DeleteCommand { get; }

    public GalleryEntryViewModel? SelectedEntry
    {
        get => _selectedEntry;
        set
        {
            if (!SetProperty(ref _selectedEntry, value))
            {
                return;
            }

            LoadPreview(value);
            OnPropertyChanged(nameof(PropertiesText));
            OnPropertyChanged(nameof(HasPreview));
            OpenCommand.NotifyCanExecuteChanged();
            RevealCommand.NotifyCanExecuteChanged();
            RenameCommand.NotifyCanExecuteChanged();
        }
    }

    public BitmapImage? Preview
    {
        get => _preview;
        private set
        {
            if (SetProperty(ref _preview, value))
            {
                OnPropertyChanged(nameof(HasPreview));
            }
        }
    }

    public bool HasPreview => Preview is not null;

    public string StatusText
    {
        get => _statusText;
        private set => SetProperty(ref _statusText, value);
    }

    public bool IsSortedByTaken => _sortKey == GallerySortKey.Taken;

    public bool IsSortedByName => _sortKey == GallerySortKey.Name;

    public bool IsSortedBySize => _sortKey == GallerySortKey.Size;

    public bool IsSortDescending => _sortDescending;

    /// <summary>Текущий порядок виден в шапке списка — иначе о нём негде узнать.</summary>
    public string SortText
    {
        get
        {
            var key = _sortKey switch
            {
                GallerySortKey.Name => "имени",
                GallerySortKey.Size => "размеру",
                _ => "дате"
            };

            return $"по {key} {(_sortDescending ? "↓" : "↑")}";
        }
    }

    public bool IsAtRoot => string.Equals(
        Path.TrimEndingDirectorySeparator(_currentDirectory),
        Path.TrimEndingDirectorySeparator(_rootDirectory),
        StringComparison.OrdinalIgnoreCase);

    public string SummaryText
    {
        get
        {
            var files = Entries.Count(entry => !entry.IsFolder);
            var folders = Entries.Count - files;
            var totalBytes = Entries.Where(entry => !entry.IsFolder).Sum(entry => entry.SizeBytes);

            var parts = new List<string>(3);
            if (folders > 0)
            {
                parts.Add($"папок: {folders}");
            }

            parts.Add($"снимков: {files}");

            if (files > 0)
            {
                parts.Add(GalleryEntryViewModel.FormatSize(totalBytes));
            }

            return string.Join(" · ", parts);
        }
    }

    /// <summary>Свойства под превью: для одного файла подробно, для набора — итог.</summary>
    public string PropertiesText
    {
        get
        {
            if (SelectedEntries.Count > 1)
            {
                var bytes = SelectedEntries.Where(entry => !entry.IsFolder).Sum(entry => entry.SizeBytes);
                return $"Выделено: {SelectedEntries.Count} · {GalleryEntryViewModel.FormatSize(bytes)}";
            }

            if (SelectedEntry is null)
            {
                return string.Empty;
            }

            if (SelectedEntry.IsFolder)
            {
                return $"Папка · {SelectedEntry.DisplayName}";
            }

            var parts = new List<string>(4)
            {
                SelectedEntry.DisplayName,
                SelectedEntry.Timestamp.ToString("dd.MM.yyyy HH:mm:ss")
            };

            if (_pixelSizeText.Length > 0)
            {
                parts.Add(_pixelSizeText);
            }

            parts.Add(SelectedEntry.SizeText);

            return string.Join("  ·  ", parts);
        }
    }

    public void ActivateSelected()
    {
        // Выделено несколько — открываем все снимки, как это делает кнопка съёмки
        // во вкладке «Устройства». Папки в наборе пропускаем: заходить можно в одну.
        if (SelectedEntries.Count > 1)
        {
            foreach (var file in SelectedEntries.Where(entry => !entry.IsFolder).ToArray())
            {
                OpenFile(file);
            }

            return;
        }

        if (SelectedEntry is not { } entry)
        {
            return;
        }

        if (entry.IsFolder)
        {
            Navigate(entry.FullPath);
            return;
        }

        OpenFile(entry);
    }

    private void OpenFile(GalleryEntryViewModel entry)
    {
        try
        {
            Process.Start(new ProcessStartInfo(entry.FullPath) { UseShellExecute = true });
        }
        catch (Exception exception)
        {
            StatusText = $"Не открылось: {exception.Message}";
        }
    }

    public void Navigate(string directory)
    {
        _currentDirectory = directory;
        SelectedEntry = null;
        Refresh();
        OnPropertyChanged(nameof(IsAtRoot));

        // Слежение привязано к конкретной папке — переносим вместе с переходом.
        StartWatching();
    }

    // ── слежение за папкой ───────────────────────────────────────────────

    private void StartWatching()
    {
        StopWatching();

        try
        {
            _watcher = new FileSystemWatcher(_currentDirectory)
            {
                NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName | NotifyFilters.Size | NotifyFilters.LastWrite,
                IncludeSubdirectories = false
            };

            _watcher.Created += OnFolderChanged;
            _watcher.Deleted += OnFolderChanged;
            _watcher.Changed += OnFolderChanged;
            _watcher.Renamed += OnFolderChanged;
            _watcher.Error += OnWatcherError;
            _watcher.EnableRaisingEvents = true;
        }
        catch (Exception exception)
        {
            _watcher = null;
            StatusText = $"Слежение за папкой не включилось: {exception.Message}. Обновляй по F5.";
        }
    }

    private void StopWatching()
    {
        if (_watcher is null)
        {
            return;
        }

        _watcher.EnableRaisingEvents = false;
        _watcher.Created -= OnFolderChanged;
        _watcher.Deleted -= OnFolderChanged;
        _watcher.Changed -= OnFolderChanged;
        _watcher.Renamed -= OnFolderChanged;
        _watcher.Error -= OnWatcherError;
        _watcher.Dispose();
        _watcher = null;
    }

    /// <summary>Событие приходит из потока слежения — трогать список оттуда нельзя.</summary>
    private void OnFolderChanged(object sender, FileSystemEventArgs e)
    {
        _dispatcher.BeginInvoke(DispatcherPriority.Background, RestartSyncTimer);
    }

    private void OnWatcherError(object sender, ErrorEventArgs e)
    {
        _dispatcher.BeginInvoke(DispatcherPriority.Background, () =>
        {
            StopWatching();

            // Частая причина — папку удалили целиком. Обновление это заметит и уведёт выше.
            Refresh();

            if (_watcher is null)
            {
                StatusText = "Слежение за папкой прервалось. Обновляй по F5.";
                StartWatching();
            }
        });
    }

    private void RestartSyncTimer()
    {
        _syncTimer.Stop();
        _syncTimer.Start();
    }

    private void OnSyncTick(object? sender, EventArgs e)
    {
        _syncTimer.Stop();

        // Переименование по месту не должно прерываться обновлением списка.
        if (Entries.Any(entry => entry.IsEditing))
        {
            return;
        }

        Refresh();
    }

    public void Dispose()
    {
        StopWatching();
        _syncTimer.Stop();

        // Настройки могли не успеть уйти на диск — дописываем на закрытии.
        if (_saveTimer.IsEnabled)
        {
            _saveTimer.Stop();
            OnSaveTick(null, EventArgs.Empty);
        }
    }

    private void SortBy(GallerySortKey key)
    {
        // Повторный выбор той же колонки переворачивает порядок — как в проводнике.
        SetSort(key, key == _sortKey ? !_sortDescending : key == GallerySortKey.Taken);
    }

    private void SetSort(GallerySortKey key, bool descending)
    {
        _sortKey = key;
        _sortDescending = descending;

        OnPropertyChanged(nameof(IsSortedByTaken));
        OnPropertyChanged(nameof(IsSortedByName));
        OnPropertyChanged(nameof(IsSortedBySize));
        OnPropertyChanged(nameof(IsSortDescending));
        OnPropertyChanged(nameof(SortText));

        Refresh();
        SaveSettingsSoon();
    }

    private void RevealSelected()
    {
        if (SelectedEntry is not { } entry)
        {
            return;
        }

        try
        {
            Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{entry.FullPath}\""));
        }
        catch (Exception exception)
        {
            StatusText = $"Проводник не открылся: {exception.Message}";
        }
    }

    // ── операции ─────────────────────────────────────────────────────────

    /// <summary>
    /// Папка создаётся сразу с рабочим именем и тут же встаёт в режим ввода — как в
    /// проводнике. Так не нужен ни диалог, ни отдельный путь отмены.
    /// </summary>
    private void CreateFolder()
    {
        var name = "Новая папка";
        var index = 2;

        while (Directory.Exists(Path.Combine(_currentDirectory, name)))
        {
            name = $"Новая папка ({index++})";
        }

        var result = _library.CreateFolder(_currentDirectory, name);
        StatusText = result.Message;

        if (!result.IsSuccess)
        {
            return;
        }

        Refresh(result.Paths.FirstOrDefault());

        if (SelectedEntry is { } created)
        {
            BeginRename(created);
        }
    }

    private void BeginRename()
    {
        if (SelectedEntry is { } entry)
        {
            BeginRename(entry);
        }
    }

    public void BeginRename(GalleryEntryViewModel entry)
    {
        foreach (var other in Entries)
        {
            other.IsEditing = false;
        }

        entry.EditName = entry.DisplayName;
        entry.IsEditing = true;
    }

    public void CommitRename(GalleryEntryViewModel entry)
    {
        if (!entry.IsEditing)
        {
            return;
        }

        entry.IsEditing = false;

        var name = entry.EditName.Trim();

        if (name.Length == 0 || string.Equals(name, entry.DisplayName, StringComparison.Ordinal))
        {
            return;
        }

        var result = _library.Rename(entry.FullPath, name);
        StatusText = result.Message;

        Refresh(result.IsSuccess ? result.Paths.FirstOrDefault() : entry.FullPath);
    }

    public void CancelRename(GalleryEntryViewModel entry)
    {
        entry.IsEditing = false;
        entry.EditName = entry.DisplayName;
    }

    private void Delete()
    {
        var doomed = SelectedEntries.Select(entry => entry.FullPath).ToArray();

        if (doomed.Length == 0)
        {
            return;
        }

        // Превью держит удаляемый снимок на экране — снимаем выделение до операции.
        SelectedEntry = null;

        var result = _library.Delete(doomed);
        StatusText = result.Message;

        Refresh();
    }

    private void MoveTo(GalleryMoveTargetViewModel? target)
    {
        if (target is null)
        {
            return;
        }

        var moving = SelectedEntries.Select(entry => entry.FullPath).ToArray();

        if (moving.Length == 0)
        {
            return;
        }

        SelectedEntry = null;

        var result = _library.Move(moving, target.FullPath);
        StatusText = result.Message;

        Refresh();
    }

    private void Refresh()
    {
        Refresh(null);
    }

    /// <summary>
    /// Перечитывает папку и сводит список к прочитанному. Строки не пересоздаются:
    /// иначе при обновлении раз в несколько секунд прокрутка прыгала бы к началу,
    /// а выделение слетало.
    /// </summary>
    private void Refresh(string? pathToSelect)
    {
        if (ReadFolder() is not { } desired)
        {
            return;
        }

        ApplyEntries(desired);

        RebuildCrumbs();
        RebuildMoveTargets();

        if (pathToSelect is not null &&
            Entries.FirstOrDefault(entry => string.Equals(entry.FullPath, pathToSelect, StringComparison.OrdinalIgnoreCase)) is { } focused)
        {
            SelectedEntry = focused;
        }

        OnPropertyChanged(nameof(SummaryText));
        OnPropertyChanged(nameof(HasEntries));
        OnPropertyChanged(nameof(EmptyStateMessage));
    }

    /// <summary>Читает папку в нужном порядке. Null — прочитать не удалось.</summary>
    private List<GalleryEntryViewModel>? ReadFolder()
    {
        try
        {
            // Создаём только корень — он может отсутствовать до первого снимка.
            // Текущую папку не воссоздаём: удалённая снаружи, она иначе воскресала бы.
            Directory.CreateDirectory(_rootDirectory);

            if (!Directory.Exists(_currentDirectory))
            {
                StatusText = "Папку удалили — вернулись на уровень выше.";
                _currentDirectory = NearestExistingFolder(_currentDirectory);
                StartWatching();
                OnPropertyChanged(nameof(IsAtRoot));
            }

            var result = new List<GalleryEntryViewModel>();

            foreach (var directory in Directory.EnumerateDirectories(_currentDirectory)
                         .OrderBy(Path.GetFileName, StringComparer.CurrentCultureIgnoreCase))
            {
                var info = new DirectoryInfo(directory);
                result.Add(new GalleryEntryViewModel(
                    info.FullName,
                    info.Name,
                    isFolder: true,
                    info.LastWriteTime,
                    0,
                    info.Name));
            }

            var files = Directory.EnumerateFiles(_currentDirectory)
                .Where(path => ImageExtensions.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase))
                .Select(path => new FileInfo(path))
                .Select(Describe);

            result.AddRange(Sort(files));
            return result;
        }
        catch (Exception exception)
        {
            StatusText = $"Папка не прочиталась: {exception.Message}";
            return null;
        }
    }

    /// <summary>
    /// Сводит список к прочитанному минимальными правками: уцелевшие строки остаются
    /// теми же объектами, поэтому выделение и режим ввода переживают обновление.
    /// </summary>
    private void ApplyEntries(List<GalleryEntryViewModel> desired)
    {
        var existing = new Dictionary<string, GalleryEntryViewModel>(StringComparer.OrdinalIgnoreCase);

        foreach (var entry in Entries)
        {
            existing[entry.FullPath] = entry;
        }

        for (var index = 0; index < desired.Count; index++)
        {
            // Размер и время сверяем тоже: файл могли перезаписать под тем же именем.
            if (existing.TryGetValue(desired[index].FullPath, out var reusable) &&
                reusable.SizeBytes == desired[index].SizeBytes &&
                reusable.Timestamp == desired[index].Timestamp)
            {
                desired[index] = reusable;
            }
        }

        for (var index = 0; index < desired.Count; index++)
        {
            if (index < Entries.Count && ReferenceEquals(Entries[index], desired[index]))
            {
                continue;
            }

            var current = IndexOfSame(desired[index], index);

            if (current >= 0)
            {
                Entries.Move(current, index);
            }
            else
            {
                Entries.Insert(index, desired[index]);
            }
        }

        while (Entries.Count > desired.Count)
        {
            Entries.RemoveAt(Entries.Count - 1);
        }
    }

    /// <summary>Ближайшая существующая папка вверх по дереву, но не выше корня.</summary>
    private string NearestExistingFolder(string directory)
    {
        var current = Directory.GetParent(directory);

        while (current is not null)
        {
            if (Directory.Exists(current.FullName) && IsInsideRoot(current.FullName))
            {
                return current.FullName;
            }

            current = current.Parent;
        }

        return _rootDirectory;
    }

    private bool IsInsideRoot(string path)
    {
        var full = Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
        var root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(_rootDirectory));

        return string.Equals(full, root, StringComparison.OrdinalIgnoreCase) ||
               full.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    }

    private int IndexOfSame(GalleryEntryViewModel entry, int from)
    {
        for (var index = from; index < Entries.Count; index++)
        {
            if (ReferenceEquals(Entries[index], entry))
            {
                return index;
            }
        }

        return -1;
    }

    /// <summary>
    /// Всё дерево папок от корня: иначе в глубоко вложенную папку пришлось бы
    /// переносить в несколько приёмов, заходя в каждую по дороге.
    /// </summary>
    private void RebuildMoveTargets()
    {
        MoveTargets.Clear();

        try
        {
            MoveTargets.Add(BuildMoveTarget(_rootDirectory, "Скриншоты", depth: 0));
        }
        catch (Exception exception)
        {
            StatusText = $"Дерево папок не прочиталось: {exception.Message}";
        }

        OnPropertyChanged(nameof(HasMoveTargets));
    }

    private static GalleryMoveTargetViewModel BuildMoveTarget(string directory, string title, int depth)
    {
        // Предохранитель от связок каталогов, зацикленных сами на себя.
        var subdirectories = depth >= 16
            ? []
            : Directory.EnumerateDirectories(directory)
                .OrderBy(Path.GetFileName, StringComparer.CurrentCultureIgnoreCase)
                .Select(path => BuildMoveTarget(path, Path.GetFileName(path), depth + 1))
                .ToArray();

        if (subdirectories.Length == 0)
        {
            return new GalleryMoveTargetViewModel(title, directory);
        }

        // У пункта с вложенными щелчок раскрывает подменю, а не выполняет команду —
        // поэтому саму папку выбираем первым пунктом внутри.
        var children = new List<GalleryMoveTargetViewModel>(subdirectories.Length + 1)
        {
            new("в эту папку", directory)
        };

        children.AddRange(subdirectories);

        return new GalleryMoveTargetViewModel(title, directory, children);
    }

    /// <summary>Папки в сортировке не участвуют — они всегда наверху, как в проводнике.</summary>
    private IEnumerable<GalleryEntryViewModel> Sort(IEnumerable<GalleryEntryViewModel> files)
    {
        return _sortKey switch
        {
            GallerySortKey.Name => _sortDescending
                ? files.OrderByDescending(entry => entry.DisplayName, StringComparer.CurrentCultureIgnoreCase)
                : files.OrderBy(entry => entry.DisplayName, StringComparer.CurrentCultureIgnoreCase),
            GallerySortKey.Size => _sortDescending
                ? files.OrderByDescending(entry => entry.SizeBytes)
                : files.OrderBy(entry => entry.SizeBytes),
            _ => _sortDescending
                ? files.OrderByDescending(entry => entry.Timestamp)
                : files.OrderBy(entry => entry.Timestamp)
        };
    }

    /// <summary>
    /// Имя снимка собрано как «дата_время_устройство». Разбираем его, чтобы в списке
    /// стояло имя устройства, а не двадцать одинаковых цифр подряд.
    /// </summary>
    private static GalleryEntryViewModel Describe(FileInfo file)
    {
        var bareName = Path.GetFileNameWithoutExtension(file.Name);
        var parts = bareName.Split('_', 3);

        if (parts.Length == 3 &&
            DateTimeOffset.TryParseExact(
                $"{parts[0]} {parts[1].Replace('-', ':')}",
                "yyyy-MM-dd HH:mm:ss",
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out var taken))
        {
            return new GalleryEntryViewModel(file.FullName, parts[2], isFolder: false, taken, file.Length, file.Name);
        }

        // Файл переименовали вручную — показываем как есть.
        return new GalleryEntryViewModel(file.FullName, bareName, isFolder: false, file.LastWriteTime, file.Length, file.Name);
    }

    private void RebuildCrumbs()
    {
        Crumbs.Clear();

        var segments = new List<GalleryCrumbViewModel>();
        var current = _currentDirectory;

        while (true)
        {
            var title = string.Equals(
                Path.TrimEndingDirectorySeparator(current),
                Path.TrimEndingDirectorySeparator(_rootDirectory),
                StringComparison.OrdinalIgnoreCase)
                ? "Скриншоты"
                : Path.GetFileName(Path.TrimEndingDirectorySeparator(current));

            segments.Insert(0, new GalleryCrumbViewModel(title, current));

            if (string.Equals(
                    Path.TrimEndingDirectorySeparator(current),
                    Path.TrimEndingDirectorySeparator(_rootDirectory),
                    StringComparison.OrdinalIgnoreCase) ||
                Directory.GetParent(current) is not { } parent)
            {
                break;
            }

            current = parent.FullName;
        }

        for (var index = 0; index < segments.Count; index++)
        {
            segments[index].IsFirst = index == 0;
            segments[index].IsLast = index == segments.Count - 1;
            Crumbs.Add(segments[index]);
        }
    }

    private void LoadPreview(GalleryEntryViewModel? entry)
    {
        _pixelSizeText = string.Empty;

        if (entry is null || entry.IsFolder)
        {
            Preview = null;
            return;
        }

        try
        {
            var image = new BitmapImage();
            image.BeginInit();

            // Файл читается целиком в память и тут же закрывается. Без этого WPF держит
            // его открытым, и переименование с удалением упрутся в «файл занят».
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.CreateOptions = BitmapCreateOptions.IgnoreImageCache;
            image.UriSource = new Uri(entry.FullPath);
            image.EndInit();
            image.Freeze();

            _pixelSizeText = $"{image.PixelWidth}×{image.PixelHeight}";
            Preview = image;
        }
        catch (Exception exception)
        {
            Preview = null;
            StatusText = $"Снимок не открылся: {exception.Message}";
        }
    }

    private void OnSelectedEntriesChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        OnPropertyChanged(nameof(PropertiesText));
        DeleteCommand.NotifyCanExecuteChanged();
    }
}

/// <summary>
/// Узел дерева папок для переноса. Папка с вложенными раскрывается подменю, а сама
/// выбирается через первый пункт внутри: WPF не отдаёт команду по щелчку на пункте,
/// у которого есть дочерние.
/// </summary>
public sealed class GalleryMoveTargetViewModel
{
    public GalleryMoveTargetViewModel(string title, string fullPath, IReadOnlyList<GalleryMoveTargetViewModel>? children = null)
    {
        Title = title;
        FullPath = fullPath;
        Children = children ?? [];
    }

    public string Title { get; }

    public string FullPath { get; }

    public IReadOnlyList<GalleryMoveTargetViewModel> Children { get; }
}

public enum GallerySortKey
{
    Taken = 0,
    Name = 1,
    Size = 2
}

public sealed class GalleryCrumbViewModel : ObservableObject
{
    private bool _isFirst;
    private bool _isLast;

    public GalleryCrumbViewModel(string title, string fullPath)
    {
        Title = title;
        FullPath = fullPath;
    }

    public string Title { get; }

    public string FullPath { get; }

    /// <summary>Корневой сегмент рисуется как заголовок вкладки.</summary>
    public bool IsFirst
    {
        get => _isFirst;
        set => SetProperty(ref _isFirst, value);
    }

    public bool IsLast
    {
        get => _isLast;
        set => SetProperty(ref _isLast, value);
    }
}
