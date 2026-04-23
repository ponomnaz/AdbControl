namespace AdbControl.Application.Logcat;

public sealed record DeviceLogcatSessionStartResult(bool IsSuccess, string Message, IDeviceLogcatSession? Session)
{
    public static DeviceLogcatSessionStartResult Success(IDeviceLogcatSession session) => new(true, string.Empty, session);

    public static DeviceLogcatSessionStartResult Failure(string message) => new(false, message, null);
}
