using AdbControl.Core.Devices;

namespace AdbControl.Application.Devices;

public interface IDeviceActionService
{
    Task<DeviceActionBatchResult> TogglePowerAsync(IReadOnlyList<TvDeviceProfile> devices, CancellationToken cancellationToken = default);

    Task<DeviceActionBatchResult> RebootAsync(IReadOnlyList<TvDeviceProfile> devices, CancellationToken cancellationToken = default);
}
