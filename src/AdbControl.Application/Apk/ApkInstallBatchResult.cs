namespace AdbControl.Application.Apk;

public sealed record ApkInstallBatchResult(
    int RequestedOperationCount,
    int SuccessCount,
    int FailureCount,
    IReadOnlyList<ApkInstallOperationResult> Operations);
