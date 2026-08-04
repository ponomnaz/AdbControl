using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Windows.Media.Imaging;
using AdbControl.Application.Common;
using AdbControl.Application.Devices;

namespace AdbControl.Tools.Gallery.ViewModels;

/// <summary>
/// Болванка вкладки: список и превью читают настоящую папку снимков, но ничего в ней
/// не меняют. Действия, которые правят диск, пока только сообщают о себе в статусе.
/// </summary>
public sealed class ScreenshotGalleryToolViewModel : ObservableObject
{
    private static readonly string[] ImageExtensions = [".png", ".jpg", ".jpeg", ".bmp", ".webp"];

    private readonly string _rootDirectory;
    private string _currentDirectory;
    private GalleryEntryViewModel? _selectedEntry;
    private BitmapImage? _preview;
    private string _statusText = string.Empty;
    private string _pixelSizeText = string.Empty;
    private GallerySortKey _sortKey = GallerySortKey.Taken;
    private bool _sortDescending = true;

    public ScreenshotGalleryToolViewModel(IDeviceScreenshotService screenshots)
    {
        _rootDirectory = screenshots.ScreenshotsDirectory;
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
        NewFolderCommand = new RelayCommand(() => AnnouncePending("Создание папки"));
        RenameCommand = new RelayCommand(() => AnnouncePending("Переименование"), () => SelectedEntry is not null);
        MoveCommand = new RelayCommand(() => AnnouncePending("Перемещение"), () => SelectedEntries.Count > 0);
        DeleteCommand = new RelayCommand(() => AnnouncePending("Удаление"), () => SelectedEntries.Count > 0);

        SelectedEntries.CollectionChanged += OnSelectedEntriesChanged;

        Refresh();
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

    public RelayCommand MoveCommand { get; }

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
        if (SelectedEntry is not { } entry)
        {
            return;
        }

        if (entry.IsFolder)
        {
            Navigate(entry.FullPath);
            return;
        }

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
        Refresh();
        OnPropertyChanged(nameof(IsAtRoot));
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

    private void AnnouncePending(string action)
    {
        StatusText = $"{action} появится на следующем этапе — сейчас это болванка раскладки.";
    }

    private void Refresh()
    {
        Entries.Clear();
        SelectedEntry = null;

        try
        {
            Directory.CreateDirectory(_currentDirectory);

            foreach (var directory in Directory.EnumerateDirectories(_currentDirectory).OrderBy(Path.GetFileName, StringComparer.CurrentCultureIgnoreCase))
            {
                var info = new DirectoryInfo(directory);
                Entries.Add(new GalleryEntryViewModel(
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

            foreach (var entry in Sort(files))
            {
                Entries.Add(entry);
            }

            StatusText = string.Empty;
        }
        catch (Exception exception)
        {
            StatusText = $"Папка не прочиталась: {exception.Message}";
        }

        RebuildCrumbs();
        OnPropertyChanged(nameof(SummaryText));
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
        MoveCommand.NotifyCanExecuteChanged();
        DeleteCommand.NotifyCanExecuteChanged();
    }
}

public enum GallerySortKey
{
    Taken = 0,
    Name = 1,
    Size = 2
}

public sealed class GalleryCrumbViewModel : ObservableObject
{
    private bool _isLast;

    public GalleryCrumbViewModel(string title, string fullPath)
    {
        Title = title;
        FullPath = fullPath;
    }

    public string Title { get; }

    public string FullPath { get; }

    public bool IsLast
    {
        get => _isLast;
        set => SetProperty(ref _isLast, value);
    }
}
