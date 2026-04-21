using AdbControl.Core.Devices;

namespace AdbControl.Application.Apk;

public interface IApkDevicePackageService
{
    Task<DevicePackagesBatchResult> GetInstalledPackagesAsync(
        IReadOnlyList<TvDeviceProfile> devices,
        CancellationToken cancellationToken = default);

    Task<PackageUninstallBatchResult> UninstallAsync(
        string packageName,
        IReadOnlyList<TvDeviceProfile> devices,
        CancellationToken cancellationToken = default);
}
