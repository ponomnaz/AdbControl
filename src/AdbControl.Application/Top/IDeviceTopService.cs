using AdbControl.Core.Devices;

namespace AdbControl.Application.Top;

public interface IDeviceTopService
{
    /// <summary>
    /// Снимок top. <paramref name="processLimit"/> ограничивает выдачу самыми нагруженными
    /// процессами: полный список на телевизоре — три сотни строк, а сам сбор заметно грузит устройство.
    /// </summary>
    Task<DeviceTopSnapshotResult> CaptureAsync(
        TvDeviceProfile device,
        int? processLimit = null,
        CancellationToken cancellationToken = default);
}
