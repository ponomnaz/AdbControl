using AdbControl.Application.Apk;
using AdbControl.Core.Devices;

namespace AdbControl.Infrastructure.Devices;

public sealed class AdbApkDeploymentService : IApkDeploymentService
{
    private readonly AdbProcessRunner _adbProcessRunner;

    public AdbApkDeploymentService(AdbProcessRunner adbProcessRunner)
    {
        _adbProcessRunner = adbProcessRunner;
    }

    public async Task<ApkInstallBatchResult> InstallAsync(
        IReadOnlyList<ApkLibraryEntry> apks,
        IReadOnlyList<TvDeviceProfile> devices,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(apks);
        ArgumentNullException.ThrowIfNull(devices);

        var operations = new List<ApkInstallOperationResult>();

        foreach (var device in devices)
        {
            var deviceTarget = GetDeviceTarget(device);
            if (string.IsNullOrWhiteSpace(deviceTarget))
            {
                continue;
            }

            foreach (var apk in apks)
            {
                if (string.IsNullOrWhiteSpace(apk.FilePath) || !File.Exists(apk.FilePath))
                {
                    operations.Add(new ApkInstallOperationResult(
                        apk.DisplayName,
                        device.DisplayName,
                        deviceTarget,
                        false,
                        "APK не найден."));
                    continue;
                }

                var escapedPath = apk.FilePath.Replace("\"", "\\\"");
                var result = await _adbProcessRunner.RunAsync(
                    $"-s {deviceTarget} install -r \"{escapedPath}\"",
                    cancellationToken);

                operations.Add(new ApkInstallOperationResult(
                    apk.DisplayName,
                    device.DisplayName,
                    deviceTarget,
                    IsSuccess(result),
                    TranslateMessage(result)));
            }
        }

        var successCount = operations.Count(static x => x.IsSuccess);

        return new ApkInstallBatchResult(
            operations.Count,
            successCount,
            operations.Count - successCount,
            operations);
    }

    private static string GetDeviceTarget(TvDeviceProfile device)
    {
        return string.IsNullOrWhiteSpace(device.NetworkEndpoint)
            ? device.Id
            : device.NetworkEndpoint;
    }

    private static bool IsSuccess(AdbProcessResult result)
    {
        if (!result.Started || result.ExitCode != 0)
        {
            return false;
        }

        var raw = BuildRawMessage(result.Stdout, result.Stderr);
        return raw.Contains("Success", StringComparison.OrdinalIgnoreCase) ||
               string.IsNullOrWhiteSpace(raw);
    }

    private static string TranslateMessage(AdbProcessResult result)
    {
        if (!result.Started)
        {
            return "adb.exe не найден.";
        }

        var raw = BuildRawMessage(result.Stdout, result.Stderr);
        if (string.IsNullOrWhiteSpace(raw))
        {
            return result.ExitCode == 0 ? "Установлено." : "Команда завершилась с ошибкой.";
        }

        if (raw.Contains("Success", StringComparison.OrdinalIgnoreCase))
        {
            return "Установлено.";
        }

        if (raw.Contains("INSTALL_FAILED_ALREADY_EXISTS", StringComparison.OrdinalIgnoreCase))
        {
            return "Уже установлено.";
        }

        if (raw.Contains("INSTALL_FAILED_VERSION_DOWNGRADE", StringComparison.OrdinalIgnoreCase))
        {
            return "Версия ниже установленной.";
        }

        if (raw.Contains("INSTALL_FAILED_UPDATE_INCOMPATIBLE", StringComparison.OrdinalIgnoreCase))
        {
            return "Несовместимое обновление.";
        }

        if (raw.Contains("device offline", StringComparison.OrdinalIgnoreCase))
        {
            return "Устройство офлайн.";
        }

        return raw.Trim();
    }

    private static string BuildRawMessage(string stdout, string stderr)
    {
        return string.Join(" ", new[] { stdout, stderr }
            .Where(static value => !string.IsNullOrWhiteSpace(value))
            .Select(static value => value.Trim()));
    }
}
