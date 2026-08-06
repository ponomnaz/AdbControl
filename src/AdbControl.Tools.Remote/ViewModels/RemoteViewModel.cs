using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.IO;
using AdbControl.Application.Common;
using AdbControl.Application.Devices;
using AdbControl.Application.Remote;
using AdbControl.Core.Devices;

namespace AdbControl.Tools.Remote.ViewModels;

public sealed class RemoteViewModel : ObservableObject, IDisposable
{
    private readonly DeviceInventoryState _deviceInventory;
    private readonly DeviceAliasCatalog _deviceAliases;
    private readonly IRemoteControlService _remoteControl;
    private readonly bool _isScrcpyAvailable;
    private RemoteDeviceOptionViewModel? _selectedDevice;
    private bool _isMirroring;
    private bool _isConnecting;
    private bool _isDisposed;
    private RemoteSizeOptionViewModel? _selectedSize;
    private RemoteDensityOptionViewModel? _selectedDensity;
    private DeviceDisplayState? _displayState;
    private string _displayStatus = string.Empty;

    /// <summary>Пока подставляем прочитанное с устройства, обратно применять нельзя.</summary>
    private bool _isSyncingDisplay;

    public RemoteViewModel(
        DeviceInventoryState deviceInventory,
        DeviceAliasCatalog deviceAliases,
        IRemoteControlService remoteControl)
    {
        _deviceInventory = deviceInventory;
        _deviceAliases = deviceAliases;
        _remoteControl = remoteControl;
        _isScrcpyAvailable = IsScrcpyInPath();

        SendKeyCommand = new RelayCommand<int>(
            keyCode => _ = SendKeyAsync(keyCode),
            _ => CanSendKey());

        StartMirroringCommand = new RelayCommand(
            () => IsMirroring = true,
            () => !_isDisposed && !_isMirroring && _isScrcpyAvailable && SelectedDevice is not null);

        StopMirroringCommand = new RelayCommand(
            () => IsMirroring = false,
            () => !_isDisposed && _isMirroring);

        BuildDisplayOptions();

        _deviceInventory.KnownDevices.CollectionChanged += OnKnownDevicesChanged;
        _deviceAliases.Changed += OnAliasesChanged;

        RefreshConnectedDevices();
    }

    public ObservableCollection<RemoteDeviceOptionViewModel> ConnectedDevices { get; } = [];

    public ObservableCollection<RemoteSizeOptionViewModel> SizeOptions { get; } = [];

    public ObservableCollection<RemoteDensityOptionViewModel> DensityOptions { get; } = [];

    public RelayCommand<int> SendKeyCommand { get; }

    public RelayCommand StartMirroringCommand { get; }

    public RelayCommand StopMirroringCommand { get; }

    public RemoteDeviceOptionViewModel? SelectedDevice
    {
        get => _selectedDevice;
        set
        {
            if (!SetProperty(ref _selectedDevice, value))
            {
                return;
            }

            IsMirroring = false;
            OnPropertyChanged(nameof(TargetId));
            OnPropertyChanged(nameof(EmptyStateMessage));
            NotifyCommandStateChanged();

            _ = ReloadDisplayAsync();
        }
    }

    public RemoteSizeOptionViewModel? SelectedSize
    {
        get => _selectedSize;
        set
        {
            if (!SetProperty(ref _selectedSize, value) || _isSyncingDisplay || value is null)
            {
                return;
            }

            _ = ApplySizeAsync(value);
        }
    }

    public RemoteDensityOptionViewModel? SelectedDensity
    {
        get => _selectedDensity;
        set
        {
            if (!SetProperty(ref _selectedDensity, value) || _isSyncingDisplay || value is null)
            {
                return;
            }

            _ = ApplyDensityAsync(value);
        }
    }

    public string DisplayStatus
    {
        get => _displayStatus;
        private set => SetProperty(ref _displayStatus, value);
    }

    public bool IsDisplayControlEnabled => SelectedDevice is not null && !_isDisposed;

    public bool IsMirroring
    {
        get => _isMirroring;
        private set
        {
            if (!SetProperty(ref _isMirroring, value))
            {
                return;
            }

            _isConnecting = value;
            OnPropertyChanged(nameof(StatusText));
            NotifyCommandStateChanged();

            if (value && TargetId is not null)
            {
                _ = _remoteControl.StartSessionAsync(TargetId);
            }
            else
            {
                _remoteControl.StopSession();
            }
        }
    }

    public bool IsConnecting => _isConnecting;

    /// <summary>Called by the View when the scrcpy window has been embedded.</summary>
    public void NotifyWindowAttached()
    {
        _isConnecting = false;
        OnPropertyChanged(nameof(IsConnecting));
        OnPropertyChanged(nameof(StatusText));
    }

    public string? TargetId => _selectedDevice?.TargetId;

    public string DeviceSummary =>
        ConnectedDevices.Count == 0
            ? "Нет подключённых устройств"
            : ConnectedDevices.Count == 1
                ? "1 устройство"
                : $"Устройств: {ConnectedDevices.Count}";

    public string StatusText =>
        !_isScrcpyAvailable
            ? "scrcpy не найден в PATH — трансляция недоступна"
            : _isConnecting
                ? "Подключение…"
                : _isMirroring
                    ? "Трансляция активна"
                    : string.Empty;

    public string EmptyStateMessage =>
        !_isScrcpyAvailable
            ? "Установи scrcpy и добавь в PATH, чтобы видеть экран телевизора."
            : ConnectedDevices.Count == 0
                ? "Нет подключённых устройств."
                : SelectedDevice is null
                    ? "Выбери телевизор."
                    : "Нажми «Трансляция», чтобы увидеть экран.";

    // ── экран телевизора ─────────────────────────────────────────────────

    private void BuildDisplayOptions()
    {
        SizeOptions.Add(RemoteSizeOptionViewModel.Reset());

        foreach (var size in new[] { "3840x2160", "2560x1440", "1920x1080", "1600x900", "1280x720", "1024x576" })
        {
            SizeOptions.Add(RemoteSizeOptionViewModel.Create(size));
        }

        DensityOptions.Add(RemoteDensityOptionViewModel.Reset());

        foreach (var density in new[] { 640, 480, 420, 360, 320, 280, 240, 213, 160 })
        {
            DensityOptions.Add(RemoteDensityOptionViewModel.Create(density));
        }
    }

    private async Task ReloadDisplayAsync()
    {
        _displayState = null;
        OnPropertyChanged(nameof(IsDisplayControlEnabled));

        if (SelectedDevice is not { } device || _isDisposed)
        {
            SyncDisplaySelection();
            return;
        }

        DisplayStatus = "Читаю экран…";

        try
        {
            _displayState = await _remoteControl.ReadDisplayAsync(device.TargetId);
            DisplayStatus = _displayState is null ? "Телевизор не ответил" : string.Empty;
        }
        catch (Exception exception)
        {
            DisplayStatus = $"Экран не прочитан: {exception.Message}";
        }

        SyncDisplaySelection();
    }

    /// <summary>Подставляет в списки то, что реально стоит на телевизоре.</summary>
    private void SyncDisplaySelection()
    {
        _isSyncingDisplay = true;
        try
        {
            // Пункт сброса несёт физическое значение — по нему видно, куда возвращаемся.
            foreach (var option in SizeOptions.Where(option => option.IsReset))
            {
                option.ApplyPhysical(_displayState?.PhysicalSize);
            }

            foreach (var option in DensityOptions.Where(option => option.IsReset))
            {
                option.ApplyPhysical(_displayState?.PhysicalDensity);
            }

            var size = _displayState?.OverrideSize;
            SelectedSize = size is null
                ? SizeOptions.FirstOrDefault(option => option.IsReset)
                : SizeOptions.FirstOrDefault(option => string.Equals(option.Size, size, StringComparison.OrdinalIgnoreCase))
                  ?? AddSizeOption(size);

            var density = _displayState?.OverrideDensity;
            SelectedDensity = density is null
                ? DensityOptions.FirstOrDefault(option => option.IsReset)
                : DensityOptions.FirstOrDefault(option => option.Density == density)
                  ?? AddDensityOption(density.Value);
        }
        finally
        {
            _isSyncingDisplay = false;
        }
    }

    /// <summary>На телевизоре может стоять значение, которого нет в наборе — показываем и его.</summary>
    private RemoteSizeOptionViewModel AddSizeOption(string size)
    {
        var option = RemoteSizeOptionViewModel.Create(size);
        SizeOptions.Add(option);
        return option;
    }

    private RemoteDensityOptionViewModel AddDensityOption(int density)
    {
        var option = RemoteDensityOptionViewModel.Create(density);
        DensityOptions.Add(option);
        return option;
    }

    private async Task ApplySizeAsync(RemoteSizeOptionViewModel option)
    {
        if (SelectedDevice is not { } device)
        {
            return;
        }

        DisplayStatus = option.IsReset ? "Возвращаю размер…" : $"Ставлю {option.DisplayText}…";

        var applied = await _remoteControl.ApplySizeAsync(device.TargetId, option.Size);
        DisplayStatus = applied ? string.Empty : "Размер не применился";

        await ReloadDisplayAsync();
    }

    private async Task ApplyDensityAsync(RemoteDensityOptionViewModel option)
    {
        if (SelectedDevice is not { } device)
        {
            return;
        }

        DisplayStatus = option.IsReset ? "Возвращаю плотность…" : $"Ставлю {option.DisplayText}…";

        var applied = await _remoteControl.ApplyDensityAsync(device.TargetId, option.Density);
        DisplayStatus = applied ? string.Empty : "Плотность не применилась";

        await ReloadDisplayAsync();
    }

    private async Task SendKeyAsync(int keyCode)
    {
        if (SelectedDevice is null || _isDisposed)
        {
            return;
        }

        await _remoteControl.SendKeyEventAsync(SelectedDevice.TargetId, keyCode);
    }

    private bool CanSendKey()
    {
        return !_isDisposed && SelectedDevice is not null;
    }

    private void NotifyCommandStateChanged()
    {
        SendKeyCommand.NotifyCanExecuteChanged();
        StartMirroringCommand.NotifyCanExecuteChanged();
        StopMirroringCommand.NotifyCanExecuteChanged();
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

    private RemoteDeviceOptionViewModel BuildDeviceOption(TvDeviceProfile device)
    {
        var targetId = string.IsNullOrWhiteSpace(device.NetworkEndpoint) ? device.Id : device.NetworkEndpoint;
        var alias = _deviceAliases.GetAlias(targetId);
        var displayText = string.IsNullOrWhiteSpace(alias) ? device.DisplayName : alias;
        return new RemoteDeviceOptionViewModel(targetId, displayText);
    }

    private static bool IsScrcpyInPath()
    {
        var paths = new[]
        {
            Environment.GetEnvironmentVariable("PATH", EnvironmentVariableTarget.Machine),
            Environment.GetEnvironmentVariable("PATH", EnvironmentVariableTarget.User),
            Environment.GetEnvironmentVariable("PATH", EnvironmentVariableTarget.Process)
        };

        return paths
            .Where(p => !string.IsNullOrEmpty(p))
            .SelectMany(p => p!.Split(';', StringSplitOptions.RemoveEmptyEntries))
            .Select(dir => dir.Trim())
            .Where(dir => !string.IsNullOrEmpty(dir))
            .Any(dir =>
            {
                try { return File.Exists(Path.Combine(dir, "scrcpy.exe")); }
                catch { return false; }
            });
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
        IsMirroring = false;
        _deviceInventory.KnownDevices.CollectionChanged -= OnKnownDevicesChanged;
        _deviceAliases.Changed -= OnAliasesChanged;
        NotifyCommandStateChanged();
    }
}

public sealed record RemoteDeviceOptionViewModel(string TargetId, string DisplayText)
{
    public override string ToString() => DisplayText;
}

/// <summary>Пункт списка разрешений. Size == null — вернуть физическое разрешение.</summary>
public sealed class RemoteSizeOptionViewModel : ObservableObject
{
    private string _displayText;

    private RemoteSizeOptionViewModel(string? size, string displayText)
    {
        Size = size;
        _displayText = displayText;
    }

    public string? Size { get; }

    public bool IsReset => Size is null;

    public string DisplayText
    {
        get => _displayText;
        private set => SetProperty(ref _displayText, value);
    }

    public static RemoteSizeOptionViewModel Reset() => new(null, "Как на устройстве");

    public static RemoteSizeOptionViewModel Create(string size) =>
        new(size, size.Replace("x", "×", StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Физическое разрешение подписывается прямо в пункт сброса: у свёрнутого списка
    /// иначе не отличить «как на устройстве» размера от такого же пункта плотности.
    /// </summary>
    public void ApplyPhysical(string? physicalSize)
    {
        DisplayText = physicalSize is null
            ? "Как на устройстве"
            : $"Как на устройстве ({physicalSize.Replace("x", "×", StringComparison.OrdinalIgnoreCase)})";
    }

    public override string ToString() => DisplayText;
}

/// <summary>Пункт списка плотностей. Density == null — вернуть физическую плотность.</summary>
public sealed class RemoteDensityOptionViewModel : ObservableObject
{
    private string _displayText;

    private RemoteDensityOptionViewModel(int? density, string displayText)
    {
        Density = density;
        _displayText = displayText;
    }

    public int? Density { get; }

    public bool IsReset => Density is null;

    public string DisplayText
    {
        get => _displayText;
        private set => SetProperty(ref _displayText, value);
    }

    public static RemoteDensityOptionViewModel Reset() => new(null, "Как на устройстве");

    public static RemoteDensityOptionViewModel Create(int density) => new(density, $"{density} dpi");

    public void ApplyPhysical(int? physicalDensity)
    {
        DisplayText = physicalDensity is { } value
            ? $"Как на устройстве ({value} dpi)"
            : "Как на устройстве";
    }

    public override string ToString() => DisplayText;
}
