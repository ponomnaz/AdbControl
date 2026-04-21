namespace AdbControl.Application.Top;

public sealed record DeviceTopSnapshotResult(bool IsSuccess, string Message, DeviceTopSnapshot? Snapshot)
{
    public static DeviceTopSnapshotResult Success(DeviceTopSnapshot snapshot) => new(true, string.Empty, snapshot);

    public static DeviceTopSnapshotResult Failure(string message) => new(false, message, null);
}
