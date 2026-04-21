using AdbControl.Application.Devices;
using AdbControl.Core.Devices;

namespace AdbControl.Infrastructure.Devices;

public sealed class AdbDeviceActionService : IDeviceActionService
{
    private const string NetariumPackageName = "cs.netarium";
    private readonly AdbProcessRunner _adbProcessRunner;

    public AdbDeviceActionService(AdbProcessRunner adbProcessRunner)
    {
        _adbProcessRunner = adbProcessRunner;
    }

    public Task<DeviceActionBatchResult> TogglePowerAsync(IReadOnlyList<TvDeviceProfile> devices, CancellationToken cancellationToken = default)
    {
        return RunAsync(devices, endpoint => $"-s {endpoint} shell input keyevent 26", cancellationToken);
    }

    public Task<DeviceActionBatchResult> RebootAsync(IReadOnlyList<TvDeviceProfile> devices, CancellationToken cancellationToken = default)
    {
        return RunAsync(devices, endpoint => $"-s {endpoint} reboot", cancellationToken);
    }

    public Task<DeviceActionBatchResult> ForceStopNetariumAsync(IReadOnlyList<TvDeviceProfile> devices, CancellationToken cancellationToken = default)
    {
        return RunAsync(devices, endpoint => $"-s {endpoint} shell am force-stop {NetariumPackageName}", cancellationToken);
    }

    private async Task<DeviceActionBatchResult> RunAsync(
        IReadOnlyList<TvDeviceProfile> devices,
        Func<string, string> argumentsFactory,
        CancellationToken cancellationToken)
    {
        var successCount = 0;
        var successfulEndpoints = new List<string>();

        foreach (var device in devices)
        {
            if (string.IsNullOrWhiteSpace(device.NetworkEndpoint))
            {
                continue;
            }

            var isSuccess = await RunCommandAsync(argumentsFactory(device.NetworkEndpoint), cancellationToken);
            if (isSuccess)
            {
                successCount++;
                successfulEndpoints.Add(device.NetworkEndpoint);
            }
        }

        return new DeviceActionBatchResult(
            devices.Count,
            successCount,
            devices.Count - successCount,
            successfulEndpoints);
    }

    private async Task<bool> RunCommandAsync(string arguments, CancellationToken cancellationToken)
    {
        var result = await _adbProcessRunner.RunAsync(arguments, cancellationToken);
        return result.Started && result.ExitCode == 0;
    }
}
