using AdbControl.Core.Devices;

namespace AdbControl.Application.Top;

public interface IDeviceTopService
{
    Task<DeviceTopSnapshotResult> CaptureAsync(TvDeviceProfile device, CancellationToken cancellationToken = default);
}
