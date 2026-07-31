namespace AdbControl.Application.Devices;

public interface IScanProfileStore
{
    Task<IReadOnlyList<SavedScanProfile>> ReadAllAsync(CancellationToken cancellationToken = default);

    Task WriteAllAsync(IReadOnlyList<SavedScanProfile> profiles, CancellationToken cancellationToken = default);
}
