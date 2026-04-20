namespace AdbControl.Application.Devices;

public interface IAdbConnectionService
{
    Task<AdbConnectResult> ConnectAsync(string endpoint, CancellationToken cancellationToken = default);

    Task<AdbDisconnectResult> DisconnectAsync(string endpoint, CancellationToken cancellationToken = default);
}
