using System.ComponentModel;
using System.Diagnostics;
using AdbControl.Application.Devices;
using AdbControl.Core.Devices;

namespace AdbControl.Infrastructure.Devices;

public sealed class AdbDeviceActionService : IDeviceActionService
{
    public Task<DeviceActionBatchResult> TogglePowerAsync(IReadOnlyList<TvDeviceProfile> devices, CancellationToken cancellationToken = default)
    {
        return RunAsync(devices, endpoint => $"-s {endpoint} shell input keyevent 26", cancellationToken);
    }

    public Task<DeviceActionBatchResult> RebootAsync(IReadOnlyList<TvDeviceProfile> devices, CancellationToken cancellationToken = default)
    {
        return RunAsync(devices, endpoint => $"-s {endpoint} reboot", cancellationToken);
    }

    private static async Task<DeviceActionBatchResult> RunAsync(
        IReadOnlyList<TvDeviceProfile> devices,
        Func<string, string> argumentsFactory,
        CancellationToken cancellationToken)
    {
        var successCount = 0;

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
            }
        }

        return new DeviceActionBatchResult(
            devices.Count,
            successCount,
            devices.Count - successCount);
    }

    private static async Task<bool> RunCommandAsync(string arguments, CancellationToken cancellationToken)
    {
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = "adb",
                Arguments = arguments,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            }
        };

        try
        {
            process.Start();
        }
        catch (Win32Exception)
        {
            return false;
        }

        await process.WaitForExitAsync(cancellationToken);
        return process.ExitCode == 0;
    }
}
