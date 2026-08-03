namespace AdbControl.Application.Devices;

public sealed record ScreenshotBatchResult(
    int RequestedCount,
    int SuccessCount,
    int FailureCount,
    IReadOnlyList<string> FilePaths);
