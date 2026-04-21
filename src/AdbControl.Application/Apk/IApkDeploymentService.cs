using AdbControl.Core.Devices;

namespace AdbControl.Application.Apk;

public interface IApkDeploymentService
{
    Task<ApkInstallBatchResult> InstallAsync(
        IReadOnlyList<ApkLibraryEntry> apks,
        IReadOnlyList<TvDeviceProfile> devices,
        CancellationToken cancellationToken = default);
}
