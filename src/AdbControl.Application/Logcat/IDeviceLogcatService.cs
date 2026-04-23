using AdbControl.Core.Devices;

namespace AdbControl.Application.Logcat;

public interface IDeviceLogcatService
{
    Task<DeviceLogcatSessionStartResult> StartSessionAsync(
        TvDeviceProfile device,
        string filterArguments,
        Action<LogcatOutputLine> onLine,
        CancellationToken cancellationToken = default);

    Task<DeviceLogcatClearResult> ClearBufferAsync(
        TvDeviceProfile device,
        CancellationToken cancellationToken = default);
}
