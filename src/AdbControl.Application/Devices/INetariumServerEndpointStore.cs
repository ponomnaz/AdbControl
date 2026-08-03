namespace AdbControl.Application.Devices;

public interface INetariumServerEndpointStore
{
    /// <summary>Возвращает null, если настройка ещё не сохранялась.</summary>
    Task<NetariumServerEndpoints?> ReadAsync(CancellationToken cancellationToken = default);

    Task WriteAsync(NetariumServerEndpoints endpoints, CancellationToken cancellationToken = default);
}
