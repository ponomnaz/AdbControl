namespace AdbControl.Application.Devices;

public interface IDeviceAliasStore
{
    Task<IReadOnlyDictionary<string, string>> ReadAllAsync(CancellationToken cancellationToken = default);

    Task WriteAllAsync(IReadOnlyDictionary<string, string> aliases, CancellationToken cancellationToken = default);
}
