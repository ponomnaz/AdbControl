namespace AdbControl.Application.Devices;

public interface IAdbConnectionService
{
    Task<AdbConnectResult> ConnectAsync(string endpoint, CancellationToken cancellationToken = default);

    Task<AdbDisconnectResult> DisconnectAsync(string endpoint, CancellationToken cancellationToken = default);

    /// <summary>
    /// Все устройства из <c>adb devices</c>, включая USB и неавторизованные.
    /// </summary>
    Task<IReadOnlyList<AdbDeviceEntry>> GetConnectedDevicesAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Сопряжение по коду — обязательный шаг для беспроводной отладки Android 11+.
    /// </summary>
    Task<AdbPairResult> PairAsync(string endpoint, string pairingCode, CancellationToken cancellationToken = default);

    /// <summary>
    /// Модель подключённого устройства. Нужна для тех, у кого включена авторизация ADB:
    /// анонимной пробе сканера они баннер не отдают.
    /// </summary>
    Task<string?> GetDeviceModelAsync(string endpoint, CancellationToken cancellationToken = default);
}
