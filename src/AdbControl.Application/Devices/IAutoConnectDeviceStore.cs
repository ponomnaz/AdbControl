namespace AdbControl.Application.Devices;

public interface IAutoConnectDeviceStore
{
    Task<IReadOnlyList<string>> ReadAllAsync(CancellationToken cancellationToken = default);

    Task WriteAllAsync(IReadOnlyList<string> endpoints, CancellationToken cancellationToken = default);
}
