using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Globalization;
using AdbControl.Application.Apk;
using AdbControl.Application.Common;
using AdbControl.Application.Devices;
using AdbControl.Core.Devices;

namespace AdbControl.Tools.Apk.ViewModels;

public sealed class ApkLibraryToolViewModel : ObservableObject
{
    private readonly IApkLibraryService _apkLibrary;
    private readonly IApkDeploymentService _apkDeployment;
    private readonly IApkDevicePackageService _apkDevicePackages;
    private readonly DeviceInventoryState _deviceInventory;
    private readonly List<ApkLibraryEntry> _allEntries = [];
    private readonly List<ApkInstalledPackageItemViewModel> _allInstalledPackages = [];
    private bool _isRefreshingTargetSelection;
    private bool _isBusy;
    private bool _isDropActive;
    private string _statusText = string.Empty;
    private string _packagesStatusText = "Выбери телевизоры.";
    private string _packageSearchText = string.Empty;
    private bool _hasLoadedInstalledPackages;
    private bool _packageLoadFailedOnly;
    private bool _isPackagesTabSelected;
    private int _packagesRefreshVersion;
    private ApkSortField _sortField = ApkSortField.Name;
    private bool _isSortDescending;

    public ApkLibraryToolViewModel(
        IApkLibraryService apkLibrary,
        IApkDeploymentService apkDeployment,
        IApkDevicePackageService apkDevicePackages,
        DeviceInventoryState deviceInventory)
    {
        _apkLibrary = apkLibrary;
        _apkDeployment = apkDeployment;
        _apkDevicePackages = apkDevicePackages;
        _deviceInventory = deviceInventory;

        DeleteSelectedCommand = new RelayCommand(
            () => _ = DeleteSelectedAsync(),
            () => CanDeleteSelected());
        InstallSelectedCommand = new RelayCommand(
            () => _ = InstallSelectedAsync(),
            () => CanInstallSelected());
        RefreshPackagesCommand = new RelayCommand(
            () => _ = RefreshPackagesAsync(),
            () => CanRefreshPackages());
        UninstallSelectedPackagesCommand = new RelayCommand(
            () => _ = UninstallSelectedPackagesAsync(),
            () => CanUninstallSelectedPackages());
        SortByNameCommand = new RelayCommand(() => SetSortField(ApkSortField.Name));
        SortByDateCommand = new RelayCommand(() => SetSortField(ApkSortField.Date));
        SortBySizeCommand = new RelayCommand(() => SetSortField(ApkSortField.Size));
        ToggleSortDirectionCommand = new RelayCommand(ToggleSortDirection);

        SelectedEntries.CollectionChanged += OnSelectedEntriesChanged;
        SelectedTargetDevices.CollectionChanged += OnSelectedTargetDevicesChanged;
        SelectedInstalledPackages.CollectionChanged += OnSelectedInstalledPackagesChanged;
        _deviceInventory.KnownDevices.CollectionChanged += OnKnownDevicesChanged;

        RefreshConnectedDevices();
        _ = LoadAsync();
    }

    public ObservableCollection<ApkLibraryItemViewModel> Entries { get; } = [];

    public ObservableCollection<ApkLibraryItemViewModel> SelectedEntries { get; } = [];

    public ObservableCollection<ApkTargetDeviceViewModel> ConnectedDevices { get; } = [];

    public ObservableCollection<ApkTargetDeviceViewModel> SelectedTargetDevices { get; } = [];

    public ObservableCollection<ApkInstallResultItemViewModel> InstallResults { get; } = [];

    public ObservableCollection<ApkInstalledPackageItemViewModel> InstalledPackages { get; } = [];

    public ObservableCollection<ApkInstalledPackageItemViewModel> SelectedInstalledPackages { get; } = [];

    public RelayCommand DeleteSelectedCommand { get; }
    public RelayCommand InstallSelectedCommand { get; }
    public RelayCommand RefreshPackagesCommand { get; }
    public RelayCommand UninstallSelectedPackagesCommand { get; }
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

    public string PackagesStatusText
    {
        get => _packagesStatusText;
        private set
        {
            if (SetProperty(ref _packagesStatusText, value))
            {
                OnPropertyChanged(nameof(HasPackagesStatusText));
            }
        }
    }

    public bool HasPackagesStatusText => !string.IsNullOrWhiteSpace(PackagesStatusText);

    public bool IsPackagesTabSelected
    {
        get => _isPackagesTabSelected;
        set
        {
            if (SetProperty(ref _isPackagesTabSelected, value) && value)
            {
                _ = QueueAutoRefreshPackagesAsync();
            }
        }
    }

    public string PackageSearchText
    {
        get => _packageSearchText;
        set
        {
            if (SetProperty(ref _packageSearchText, value))
            {
                RebuildInstalledPackages();
            }
        }
    }

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

    public bool HasConnectedDevices => ConnectedDevices.Count > 0;

    public string ConnectedDevicesSummary => ConnectedDevices.Count == 0
        ? "Нет подключенных телевизоров"
        : ConnectedDevices.Count == 1
            ? "1 телевизор"
            : $"Телевизоров: {ConnectedDevices.Count}";

    public int SelectedTargetDeviceCount => SelectedTargetDevices.Count;

    public bool HasSelectedTargets => SelectedTargetDeviceCount > 0;

    public string TargetSelectionSummary => SelectedTargetDeviceCount == 0
        ? "Ничего не выбрано"
        : SelectedTargetDeviceCount == 1
            ? "Выбран 1 телевизор"
            : $"Выбрано: {SelectedTargetDeviceCount}";

    public string EmptyStateMessage => "Пока нет загруженных APK.";

    public string EmptyTargetsMessage => "Подключи телевизор, чтобы работать с ним здесь.";

    public string InstallSelectionSummary => SelectedEntries.Count == 0
        ? string.Empty
        : SelectedEntries.Count == 1
            ? SelectedEntries[0].DisplayName
            : $"APK: {SelectedEntries.Count}";

    public bool HasInstallSelectionSummary => !string.IsNullOrWhiteSpace(InstallSelectionSummary);

    public string InstallTargetSummary => SelectedTargetDeviceCount == 0
        ? "Телевизоры не выбраны"
        : SelectedTargetDeviceCount == 1
            ? SelectedTargetDevices[0].Title
            : $"Телевизоры: {SelectedTargetDeviceCount}";

    public string InstallResultSummary => InstallResults.Count == 0
        ? string.Empty
        : $"Операций: {InstallResults.Count}";

    public bool HasInstallResultSummary => !string.IsNullOrWhiteSpace(InstallResultSummary);

    public bool HasInstallResults => InstallResults.Count > 0;

    public bool HasInstalledPackages => InstalledPackages.Count > 0;

    public string InstalledPackagesSummary => !_hasLoadedInstalledPackages || _packageLoadFailedOnly
        ? string.Empty
        : $"Пакетов: {InstalledPackages.Count}";

    public string InstalledPackagesEmptyMessage => SelectedTargetDeviceCount == 0
        ? "Выбери телевизоры."
        : !_hasLoadedInstalledPackages
            ? string.Empty
            : _packageLoadFailedOnly
                ? "Не удалось получить пакеты."
                : "Пакеты не найдены.";

    public string InstalledPackageSelectionSummary => SelectedInstalledPackages.Count == 0
        ? string.IsNullOrWhiteSpace(InstalledPackagesSummary) ? string.Empty : "Выбрано: 0"
        : string.IsNullOrWhiteSpace(InstalledPackagesSummary)
            ? string.Empty
            : $"Выбрано: {SelectedInstalledPackages.Count}";

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
            StatusText = string.Empty;
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

    private async Task InstallSelectedAsync()
    {
        var selectedApks = SelectedEntries
            .Select(entry => entry.Id)
            .ToHashSet();

        var apks = _allEntries
            .Where(entry => selectedApks.Contains(entry.Id))
            .ToArray();

        var devices = SelectedTargetDevices
            .Select(entry => entry.Device)
            .ToArray();

        if (apks.Length == 0 || devices.Length == 0)
        {
            return;
        }

        try
        {
            _isBusy = true;
            NotifyCommandStateChanged();
            StatusText = apks.Length == 1 && devices.Length == 1
                ? "Установка APK..."
                : $"Установка: {apks.Length} APK на {devices.Length} ТВ";

            InstallResults.Clear();

            var operationItems = new List<(ApkInstallResultItemViewModel Item, ApkLibraryEntry Apk, TvDeviceProfile Device)>();
            foreach (var device in devices)
            {
                foreach (var apk in apks)
                {
                    var item = ApkInstallResultItemViewModel.CreatePending(
                        apk.DisplayName,
                        device.DisplayName,
                        GetSelectionKey(device));

                    InstallResults.Add(item);
                    operationItems.Add((item, apk, device));
                }
            }

            OnPropertyChanged(nameof(InstallResultSummary));
            OnPropertyChanged(nameof(HasInstallResultSummary));
            OnPropertyChanged(nameof(HasInstallResults));

            var successCount = 0;
            var failureCount = 0;

            foreach (var (item, apk, device) in operationItems)
            {
                item.MarkRunning();

                try
                {
                    var result = await _apkDeployment.InstallAsync([apk], [device]);
                    var operation = result.Operations.FirstOrDefault()
                        ?? new ApkInstallOperationResult(
                            apk.DisplayName,
                            device.DisplayName,
                            GetSelectionKey(device),
                            false,
                            "Не удалось выполнить установку.");

                    item.ApplyResult(operation);

                    if (operation.IsSuccess)
                    {
                        successCount++;
                    }
                    else
                    {
                        failureCount++;
                    }
                }
                catch (Exception ex)
                {
                    item.MarkFailed(ex.Message);
                    failureCount++;
                }
            }

            StatusText = failureCount == 0
                ? $"Установлено: {successCount}"
                : $"Установлено: {successCount}, ошибок: {failureCount}";

            await RefreshPackagesAfterMutationAsync(devices);
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

    private async Task RefreshPackagesAsync()
    {
        await RefreshPackagesAsync(isAutomatic: false);
    }

    private async Task RefreshPackagesAsync(bool isAutomatic)
    {
        var devices = SelectedTargetDevices
            .Select(static entry => entry.Device)
            .ToArray();

        if (devices.Length == 0)
        {
            return;
        }

        try
        {
            _isBusy = true;
            NotifyCommandStateChanged();
            PackagesStatusText = devices.Length == 1
                ? "Загрузка пакетов..."
                : $"Загрузка пакетов: {devices.Length} ТВ";

            SelectedInstalledPackages.Clear();
            var result = await _apkDevicePackages.GetInstalledPackagesAsync(devices);
            ApplyInstalledPackages(result);

            PackagesStatusText = result.FailureCount == 0
                ? string.Empty
                : result.SuccessCount == 0
                    ? "Не удалось получить пакеты."
                    : $"Получено: {result.SuccessCount}, ошибок: {result.FailureCount}";
        }
        catch (OperationCanceledException) when (isAutomatic)
        {
        }
        catch (Exception ex)
        {
            ResetInstalledPackages(ex.Message);
        }
        finally
        {
            _isBusy = false;
            NotifyCommandStateChanged();
        }
    }

    private async Task UninstallSelectedPackagesAsync()
    {
        var selectedPackages = SelectedInstalledPackages
            .DistinctBy(static entry => entry.PackageName, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (selectedPackages.Length == 0)
        {
            return;
        }

        var selectedDevices = SelectedTargetDevices
            .ToDictionary(static entry => entry.SelectionKey, static entry => entry.Device, StringComparer.OrdinalIgnoreCase);

        try
        {
            _isBusy = true;
            NotifyCommandStateChanged();
            PackagesStatusText = selectedPackages.Length == 1
                ? "Удаление пакета..."
                : $"Удаление пакетов: {selectedPackages.Length}";

            var operations = new List<PackageUninstallOperationResult>();

            foreach (var package in selectedPackages)
            {
                var targetDevices = package.InstalledSelectionKeys
                    .Where(selectedDevices.ContainsKey)
                    .Select(selectionKey => selectedDevices[selectionKey])
                    .ToArray();

                if (targetDevices.Length == 0)
                {
                    continue;
                }

                var result = await _apkDevicePackages.UninstallAsync(package.PackageName, targetDevices);
                operations.AddRange(result.Operations);
            }

            var successCount = operations.Count(static operation => operation.IsSuccess);
            var failureCount = operations.Count - successCount;

            await RefreshPackagesCoreAsync(selectedDevices.Values.ToArray());

            PackagesStatusText = failureCount == 0
                ? successCount == 1
                    ? "Пакет удален."
                    : $"Удалено: {successCount}"
                : $"Удалено: {successCount}, ошибок: {failureCount}";
        }
        catch (Exception ex)
        {
            PackagesStatusText = ex.Message;
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

    private async Task RefreshPackagesCoreAsync(IReadOnlyList<TvDeviceProfile> devices)
    {
        SelectedInstalledPackages.Clear();
        var result = await _apkDevicePackages.GetInstalledPackagesAsync(devices);
        ApplyInstalledPackages(result);
    }

    private async Task RefreshPackagesAfterMutationAsync(IReadOnlyList<TvDeviceProfile> devices)
    {
        if (devices.Count == 0)
        {
            ResetInstalledPackages(string.Empty);
            return;
        }

        try
        {
            await RefreshPackagesCoreAsync(devices);
            PackagesStatusText = string.Empty;
        }
        catch
        {
            ResetInstalledPackages(string.Empty);
        }
    }

    private async Task QueueAutoRefreshPackagesAsync()
    {
        if (!IsPackagesTabSelected || SelectedTargetDeviceCount == 0)
        {
            return;
        }

        var version = ++_packagesRefreshVersion;
        while (true)
        {
            await Task.Delay(180);

            if (version != _packagesRefreshVersion || !IsPackagesTabSelected || SelectedTargetDeviceCount == 0)
            {
                return;
            }

            if (!_isBusy)
            {
                break;
            }
        }

        await RefreshPackagesAsync(isAutomatic: true);
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
        OnPropertyChanged(nameof(InstallSelectionSummary));
        OnPropertyChanged(nameof(HasInstallSelectionSummary));
    }

    private void ApplyInstalledPackages(DevicePackagesBatchResult result)
    {
        var selectedCount = SelectedTargetDeviceCount;
        var deviceTitles = SelectedTargetDevices.ToDictionary(
            static device => device.SelectionKey,
            static device => device.Title,
            StringComparer.OrdinalIgnoreCase);

        var packageMap = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);

        foreach (var snapshot in result.Devices.Where(static device => device.IsSuccess))
        {
            foreach (var packageName in snapshot.Packages)
            {
                if (!packageMap.TryGetValue(packageName, out var keys))
                {
                    keys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    packageMap[packageName] = keys;
                }

                keys.Add(snapshot.DeviceTarget);
            }
        }

        _allInstalledPackages.Clear();
        _packageLoadFailedOnly = result.SuccessCount == 0 && result.FailureCount > 0;
        foreach (var packageEntry in packageMap.OrderBy(static entry => entry.Key, StringComparer.OrdinalIgnoreCase))
        {
            var installedKeys = packageEntry.Value.ToArray();
            var installedTitles = installedKeys
                .Select(key => deviceTitles.TryGetValue(key, out var title) ? title : key)
                .ToArray();

            _allInstalledPackages.Add(new ApkInstalledPackageItemViewModel(
                packageEntry.Key,
                installedKeys,
                installedTitles,
                selectedCount));
        }

        _hasLoadedInstalledPackages = true;
        RebuildInstalledPackages();
    }

    private void RebuildInstalledPackages()
    {
        var search = PackageSearchText?.Trim();
        var filtered = string.IsNullOrWhiteSpace(search)
            ? _allInstalledPackages
            : _allInstalledPackages
                .Where(item => item.PackageName.Contains(search, StringComparison.OrdinalIgnoreCase))
                .ToList();

        SelectedInstalledPackages.Clear();
        InstalledPackages.Clear();

        foreach (var item in filtered)
        {
            InstalledPackages.Add(item);
        }

        OnPropertyChanged(nameof(HasInstalledPackages));
        OnPropertyChanged(nameof(InstalledPackagesSummary));
        OnPropertyChanged(nameof(InstalledPackagesEmptyMessage));
        OnPropertyChanged(nameof(InstalledPackageSelectionSummary));
    }

    private void ResetInstalledPackages(string statusText)
    {
        _allInstalledPackages.Clear();
        _hasLoadedInstalledPackages = false;
        _packageLoadFailedOnly = false;
        SelectedInstalledPackages.Clear();
        InstalledPackages.Clear();
        PackagesStatusText = statusText;
        OnPropertyChanged(nameof(HasInstalledPackages));
        OnPropertyChanged(nameof(InstalledPackagesSummary));
        OnPropertyChanged(nameof(InstalledPackagesEmptyMessage));
        OnPropertyChanged(nameof(InstalledPackageSelectionSummary));
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

    private bool CanRefreshPackages()
    {
        return !_isBusy && SelectedTargetDeviceCount > 0;
    }

    private bool CanUninstallSelectedPackages()
    {
        return !_isBusy && SelectedTargetDeviceCount > 0 && SelectedInstalledPackages.Count > 0;
    }

    private void OnSelectedEntriesChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        OnPropertyChanged(nameof(SelectionSummary));
        OnPropertyChanged(nameof(InstallSelectionSummary));
        OnPropertyChanged(nameof(HasInstallSelectionSummary));
        NotifyCommandStateChanged();
    }

    private void OnSelectedTargetDevicesChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (_isRefreshingTargetSelection)
        {
            return;
        }

        OnPropertyChanged(nameof(SelectedTargetDeviceCount));
        OnPropertyChanged(nameof(HasSelectedTargets));
        OnPropertyChanged(nameof(TargetSelectionSummary));
        OnPropertyChanged(nameof(InstallTargetSummary));
        ResetInstalledPackages(SelectedTargetDeviceCount == 0
            ? "Выбери телевизоры."
            : string.Empty);
        if (SelectedTargetDeviceCount > 0 && IsPackagesTabSelected)
        {
            _ = QueueAutoRefreshPackagesAsync();
        }

        NotifyCommandStateChanged();
    }

    private void OnSelectedInstalledPackagesChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        OnPropertyChanged(nameof(InstalledPackageSelectionSummary));
        NotifyCommandStateChanged();
    }

    private void OnKnownDevicesChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        RefreshConnectedDevices();
    }

    private void NotifyCommandStateChanged()
    {
        DeleteSelectedCommand.NotifyCanExecuteChanged();
        InstallSelectedCommand.NotifyCanExecuteChanged();
        RefreshPackagesCommand.NotifyCanExecuteChanged();
        UninstallSelectedPackagesCommand.NotifyCanExecuteChanged();
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

    private void RefreshConnectedDevices()
    {
        var selectedKeys = SelectedTargetDevices
            .Select(static device => device.SelectionKey)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        _isRefreshingTargetSelection = true;
        try
        {
            ConnectedDevices.Clear();
            SelectedTargetDevices.Clear();

            foreach (var device in _deviceInventory.KnownDevices)
            {
                var item = new ApkTargetDeviceViewModel(
                    device,
                    GetSelectionKey(device),
                    device.DisplayName,
                    GetSecondaryText(device));

                ConnectedDevices.Add(item);

                if (selectedKeys.Contains(item.SelectionKey))
                {
                    SelectedTargetDevices.Add(item);
                }
            }
        }
        finally
        {
            _isRefreshingTargetSelection = false;
        }

        OnPropertyChanged(nameof(HasConnectedDevices));
        OnPropertyChanged(nameof(ConnectedDevicesSummary));
        OnPropertyChanged(nameof(SelectedTargetDeviceCount));
        OnPropertyChanged(nameof(HasSelectedTargets));
        OnPropertyChanged(nameof(TargetSelectionSummary));
        OnPropertyChanged(nameof(InstallTargetSummary));
        ResetInstalledPackages(SelectedTargetDeviceCount == 0
            ? "Выбери телевизоры."
            : string.Empty);
        if (SelectedTargetDeviceCount > 0 && IsPackagesTabSelected)
        {
            _ = QueueAutoRefreshPackagesAsync();
        }

        NotifyCommandStateChanged();
    }

    private static string GetSelectionKey(TvDeviceProfile device)
    {
        return string.IsNullOrWhiteSpace(device.NetworkEndpoint)
            ? device.Id
            : device.NetworkEndpoint;
    }

    private static string GetSecondaryText(TvDeviceProfile device)
    {
        if (!string.IsNullOrWhiteSpace(device.NetworkEndpoint) &&
            !string.Equals(device.DisplayName, device.NetworkEndpoint, StringComparison.OrdinalIgnoreCase))
        {
            return device.NetworkEndpoint;
        }

        return device.PreferredConnection == DeviceConnectionKind.Usb ? "USB" : "Сеть";
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

public sealed class ApkTargetDeviceViewModel : ObservableObject
{
    public ApkTargetDeviceViewModel(
        TvDeviceProfile device,
        string selectionKey,
        string title,
        string secondaryText)
    {
        Device = device;
        SelectionKey = selectionKey;
        Title = title;
        SecondaryText = secondaryText;
    }

    public TvDeviceProfile Device { get; }

    public string SelectionKey { get; }

    public string Title { get; }

    public string SecondaryText { get; }
}

public sealed class ApkInstallResultItemViewModel : ObservableObject
{
    private ApkInstallResultState _state;
    private string _message;

    private ApkInstallResultItemViewModel(
        string apkDisplayName,
        string deviceDisplayName,
        string deviceTarget,
        ApkInstallResultState state,
        string message)
    {
        ApkDisplayName = apkDisplayName;
        DeviceDisplayName = deviceDisplayName;
        DeviceTarget = deviceTarget;
        _state = state;
        _message = message;
    }

    public static ApkInstallResultItemViewModel CreatePending(string apkDisplayName, string deviceDisplayName, string deviceTarget)
    {
        return new ApkInstallResultItemViewModel(
            apkDisplayName,
            deviceDisplayName,
            deviceTarget,
            ApkInstallResultState.Pending,
            "Ждёт.");
    }

    public string ApkDisplayName { get; }

    public string DeviceDisplayName { get; }

    public string DeviceTarget { get; }

    public bool IsPending => _state == ApkInstallResultState.Pending;

    public bool IsRunning => _state == ApkInstallResultState.Running;

    public bool IsSuccess => _state == ApkInstallResultState.Success;

    public bool IsFailed => _state == ApkInstallResultState.Failed;

    public string StatusText => _state switch
    {
        ApkInstallResultState.Pending => "Ждёт",
        ApkInstallResultState.Running => "Идёт",
        ApkInstallResultState.Success => "ОК",
        _ => "Ошибка"
    };

    public string Message
    {
        get => _message;
        private set => SetProperty(ref _message, value);
    }

    public void MarkRunning()
    {
        UpdateState(ApkInstallResultState.Running, "Устанавливается...");
    }

    public void ApplyResult(ApkInstallOperationResult result)
    {
        UpdateState(result.IsSuccess ? ApkInstallResultState.Success : ApkInstallResultState.Failed, result.Message);
    }

    public void MarkFailed(string message)
    {
        UpdateState(ApkInstallResultState.Failed, message);
    }

    private void UpdateState(ApkInstallResultState state, string message)
    {
        if (_state != state)
        {
            _state = state;
            OnPropertyChanged(nameof(IsPending));
            OnPropertyChanged(nameof(IsRunning));
            OnPropertyChanged(nameof(IsSuccess));
            OnPropertyChanged(nameof(IsFailed));
            OnPropertyChanged(nameof(StatusText));
        }

        Message = message;
    }
}

public enum ApkInstallResultState
{
    Pending,
    Running,
    Success,
    Failed
}

public sealed class ApkInstalledPackageItemViewModel : ObservableObject
{
    public ApkInstalledPackageItemViewModel(
        string packageName,
        IReadOnlyList<string> installedSelectionKeys,
        IReadOnlyList<string> installedDeviceTitles,
        int selectedDeviceCount)
    {
        PackageName = packageName;
        InstalledSelectionKeys = installedSelectionKeys;
        InstalledDeviceCount = installedSelectionKeys.Count;
        SelectedDeviceCount = selectedDeviceCount;
        PresenceText = selectedDeviceCount <= 1
            ? "Установлено"
            : $"{InstalledDeviceCount}/{selectedDeviceCount}";
        MetaText = BuildMetaText(installedDeviceTitles);
    }

    public string PackageName { get; }

    public IReadOnlyList<string> InstalledSelectionKeys { get; }

    public int InstalledDeviceCount { get; }

    public int SelectedDeviceCount { get; }

    public string PresenceText { get; }

    public string MetaText { get; }

    private static string BuildMetaText(IReadOnlyList<string> installedDeviceTitles)
    {
        return installedDeviceTitles.Count switch
        {
            0 => string.Empty,
            1 => installedDeviceTitles[0],
            2 => $"{installedDeviceTitles[0]}, {installedDeviceTitles[1]}",
            _ => $"{installedDeviceTitles[0]}, {installedDeviceTitles[1]} и ещё {installedDeviceTitles.Count - 2}"
        };
    }
}
