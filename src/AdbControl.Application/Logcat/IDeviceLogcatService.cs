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

    /// <summary>
    /// Идентификатор процесса приложения или null, если оно не запущено.
    /// Нужен фильтру <c>--pid</c>: он меняется при каждом перезапуске приложения.
    /// </summary>
    Task<int?> GetProcessIdAsync(
        TvDeviceProfile device,
        string packageName,
        CancellationToken cancellationToken = default);
}
