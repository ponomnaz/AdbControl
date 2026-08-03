using AdbControl.Core.Devices;

namespace AdbControl.Application.Devices;

public interface IDeviceActionService
{
    Task<DeviceActionBatchResult> TogglePowerAsync(IReadOnlyList<TvDeviceProfile> devices, CancellationToken cancellationToken = default);

    Task<DeviceActionBatchResult> RebootAsync(IReadOnlyList<TvDeviceProfile> devices, CancellationToken cancellationToken = default);

    Task<DeviceActionBatchResult> ForceStopNetariumAsync(IReadOnlyList<TvDeviceProfile> devices, CancellationToken cancellationToken = default);

    Task<DeviceActionBatchResult> ConfigureNetariumServerAsync(IReadOnlyList<TvDeviceProfile> devices, string serverEndpoint, CancellationToken cancellationToken = default);

    /// <summary>
    /// Гасит приложение, удаляет его кэш и запускает обратно. Именно в таком порядке:
    /// живое приложение перезапишет кэш из памяти, если удалять на ходу.
    /// </summary>
    Task<CacheClearBatchResult> ClearNetariumCacheAsync(IReadOnlyList<TvDeviceProfile> devices, CancellationToken cancellationToken = default);
}
