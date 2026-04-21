namespace AdbControl.Application.Apk;

public sealed record ApkInstallOperationResult(
    string ApkDisplayName,
    string DeviceDisplayName,
    string DeviceTarget,
    bool IsSuccess,
    string Message);
