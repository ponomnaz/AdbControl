namespace AdbControl.Application.Devices;

public interface INetariumServerEndpointStore
{
    Task<string?> ReadAsync(CancellationToken cancellationToken = default);

    Task WriteAsync(string endpoint, CancellationToken cancellationToken = default);
}
