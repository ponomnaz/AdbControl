namespace AdbControl.Application.Devices;

public sealed record DeviceActionBatchResult(
    int RequestedCount,
    int SuccessCount,
    int FailureCount);
