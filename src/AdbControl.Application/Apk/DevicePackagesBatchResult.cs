namespace AdbControl.Application.Apk;

public sealed record DevicePackagesBatchResult(
    int RequestedDeviceCount,
    int SuccessCount,
    int FailureCount,
    IReadOnlyList<DevicePackagesSnapshot> Devices);

public sealed record DevicePackagesSnapshot(
    string DeviceDisplayName,
    string DeviceTarget,
    bool IsSuccess,
    string Message,
    IReadOnlyList<string> Packages);
