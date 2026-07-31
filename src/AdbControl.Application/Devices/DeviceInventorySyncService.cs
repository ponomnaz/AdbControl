using AdbControl.Core.Devices;

namespace AdbControl.Application.Devices;

/// <summary>
/// Единый источник правды о подключённых устройствах: опрашивает <c>adb devices</c>
/// и приводит инвентарь в соответствие. Заменяет разрозненные обновления по месту —
/// раньше USB-устройства не попадали в инвентарь вовсе, а сетевые обновлялись
/// только при открытом инструменте подключения.
///
/// Метод <see cref="SyncOnceAsync"/> рассчитан на вызов из потока UI: он трогает
/// наблюдаемые коллекции <see cref="DeviceInventoryState"/>.
/// </summary>
public sealed class DeviceInventorySyncService
{
    private readonly DeviceInventoryState _deviceInventory;
    private readonly IAdbConnectionService _adbConnectionService;
    private readonly DeviceAliasCatalog _deviceAliases;
    private readonly Dictionary<string, string> _models = new(StringComparer.OrdinalIgnoreCase);
    private bool _isSyncing;

    public DeviceInventorySyncService(
        DeviceInventoryState deviceInventory,
        IAdbConnectionService adbConnectionService,
        DeviceAliasCatalog deviceAliases)
    {
        _deviceInventory = deviceInventory;
        _adbConnectionService = adbConnectionService;
        _deviceAliases = deviceAliases;
    }

    public event EventHandler? Synced;

    /// <summary>Идентификаторы всех готовых к работе устройств, включая USB-серийники.</summary>
    public IReadOnlyList<string> ConnectedTargets { get; private set; } = [];

    /// <summary>
    /// Сетевые транспорты — то, что показывается в списке подключения. Сюда входят и
    /// mDNS-транспорты беспроводной отладки: подключить их по имени нельзя, но отключить
    /// можно, и без них подключённое устройство пропадало из списка совсем.
    /// </summary>
    public IReadOnlyList<string> ConnectedNetworkTargets { get; private set; } = [];

    public string? GetModel(string deviceId)
    {
        return _models.GetValueOrDefault(deviceId);
    }

    public void SetModel(string deviceId, string? model)
    {
        if (!string.IsNullOrWhiteSpace(model))
        {
            _models[deviceId] = model.Trim();
        }
    }

    public async Task SyncOnceAsync(CancellationToken cancellationToken = default)
    {
        // Тик таймера может прийти поверх ещё не завершённой синхронизации.
        if (_isSyncing)
        {
            return;
        }

        _isSyncing = true;
        try
        {
            var entries = await _adbConnectionService.GetConnectedDevicesAsync(cancellationToken);
            await CacheModelsAsync(entries, cancellationToken);

            _deviceInventory.RemoveDevicesMissingFrom(entries.Select(entry => entry.Serial));
            _deviceInventory.UpsertKnownDevices(entries.Select(ToProfile).ToArray());

            ConnectedTargets = entries
                .Where(entry => entry.IsReady)
                .Select(entry => entry.Serial)
                .ToArray();

            ConnectedNetworkTargets = entries
                .Where(entry => entry.IsReady && entry.IsNetwork)
                .Select(entry => entry.Serial)
                .ToArray();

            Synced?.Invoke(this, EventArgs.Empty);
        }
        finally
        {
            _isSyncing = false;
        }
    }

    private async Task CacheModelsAsync(IReadOnlyList<AdbDeviceEntry> entries, CancellationToken cancellationToken)
    {
        var pending = entries
            .Where(entry => entry.IsReady && !_models.ContainsKey(entry.Serial))
            .Select(entry => entry.Serial)
            .ToArray();

        if (pending.Length == 0)
        {
            return;
        }

        var resolved = await Task.WhenAll(pending.Select(async serial =>
            (Serial: serial, Model: await _adbConnectionService.GetDeviceModelAsync(serial, cancellationToken))));

        foreach (var (serial, model) in resolved)
        {
            SetModel(serial, model);
        }
    }

    private TvDeviceProfile ToProfile(AdbDeviceEntry entry)
    {
        var alias = _deviceAliases.GetAlias(entry.Serial);
        var displayName = string.IsNullOrWhiteSpace(alias)
            ? GetModel(entry.Serial) ?? entry.Serial
            : alias;

        return new TvDeviceProfile(
            entry.Serial,
            displayName,
            entry.IsConnectableEndpoint ? entry.Serial : null,
            entry.Kind,
            entry.IsReady ? DeviceReachability.Connected : DeviceReachability.Unreachable);
    }
}
