namespace AdbControl.Application.Devices;

public interface IAdbConnectionService
{
    Task<AdbConnectResult> ConnectAsync(string endpoint, CancellationToken cancellationToken = default);

    Task<AdbDisconnectResult> DisconnectAsync(string endpoint, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<string>> GetConnectedEndpointsAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Модель подключённого устройства. Нужна для тех, у кого включена авторизация ADB:
    /// анонимной пробе сканера они баннер не отдают.
    /// </summary>
    Task<string?> GetDeviceModelAsync(string endpoint, CancellationToken cancellationToken = default);
}
