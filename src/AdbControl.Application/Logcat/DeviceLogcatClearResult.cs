namespace AdbControl.Application.Logcat;

public sealed record DeviceLogcatClearResult(bool IsSuccess, string Message)
{
    public static DeviceLogcatClearResult Success(string message) => new(true, message);

    public static DeviceLogcatClearResult Failure(string message) => new(false, message);
}
