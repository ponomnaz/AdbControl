namespace AdbControl.Application.Apk;

public sealed record PackageUninstallBatchResult(
    int RequestedOperationCount,
    int SuccessCount,
    int FailureCount,
    IReadOnlyList<PackageUninstallOperationResult> Operations);

public sealed record PackageUninstallOperationResult(
    string PackageName,
    string DeviceDisplayName,
    string DeviceTarget,
    bool IsSuccess,
    string Message);
