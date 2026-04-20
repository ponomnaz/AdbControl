namespace AdbControl.Application.Devices;

public interface IDeviceDiscoveryService
{
    Task<IReadOnlyList<DiscoveredAdbEndpoint>> DiscoverAsync(CancellationToken cancellationToken = default);
}
