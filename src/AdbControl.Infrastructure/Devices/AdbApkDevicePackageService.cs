using AdbControl.Application.Apk;
using AdbControl.Core.Devices;

namespace AdbControl.Infrastructure.Devices;

public sealed class AdbApkDevicePackageService : IApkDevicePackageService
{
    private readonly AdbProcessRunner _adbProcessRunner;

    public AdbApkDevicePackageService(AdbProcessRunner adbProcessRunner)
    {
        _adbProcessRunner = adbProcessRunner;
    }

    public async Task<DevicePackagesBatchResult> GetInstalledPackagesAsync(
        IReadOnlyList<TvDeviceProfile> devices,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(devices);

        var snapshots = new List<DevicePackagesSnapshot>();

        foreach (var device in devices)
        {
            var deviceTarget = GetDeviceTarget(device);
            if (string.IsNullOrWhiteSpace(deviceTarget))
            {
                continue;
            }

            var result = await _adbProcessRunner.RunAsync(
                $"-s {deviceTarget} shell pm list packages",
                cancellationToken);

            var packages = IsSuccess(result)
                ? ParsePackages(result.Stdout)
                : Array.Empty<string>();

            snapshots.Add(new DevicePackagesSnapshot(
                device.DisplayName,
                deviceTarget,
                IsSuccess(result),
                TranslateListMessage(result, packages.Count),
                packages));
        }

        var successCount = snapshots.Count(static snapshot => snapshot.IsSuccess);

        return new DevicePackagesBatchResult(
            snapshots.Count,
            successCount,
            snapshots.Count - successCount,
            snapshots);
    }

    public async Task<PackageUninstallBatchResult> UninstallAsync(
        string packageName,
        IReadOnlyList<TvDeviceProfile> devices,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(packageName);
        ArgumentNullException.ThrowIfNull(devices);

        var operations = new List<PackageUninstallOperationResult>();

        foreach (var device in devices)
        {
            var deviceTarget = GetDeviceTarget(device);
            if (string.IsNullOrWhiteSpace(deviceTarget))
            {
                continue;
            }

            var result = await _adbProcessRunner.RunAsync(
                $"-s {deviceTarget} uninstall {packageName}",
                cancellationToken);

            operations.Add(new PackageUninstallOperationResult(
                packageName,
                device.DisplayName,
                deviceTarget,
                IsSuccess(result),
                TranslateUninstallMessage(result)));
        }

        var successCount = operations.Count(static operation => operation.IsSuccess);

        return new PackageUninstallBatchResult(
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
        return result.Started && result.ExitCode == 0;
    }

    private static IReadOnlyList<string> ParsePackages(string stdout)
    {
        return stdout
            .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(static line => line.StartsWith("package:", StringComparison.OrdinalIgnoreCase))
            .Select(static line => line["package:".Length..].Trim())
            .Where(static line => !string.IsNullOrWhiteSpace(line))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(static line => line, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static string TranslateListMessage(AdbProcessResult result, int packageCount)
    {
        if (!result.Started)
        {
            return "adb.exe не найден.";
        }

        if (result.ExitCode == 0)
        {
            return packageCount == 0
                ? "Пакеты не найдены."
                : packageCount == 1
                    ? "Найден 1 пакет."
                    : $"Найдено пакетов: {packageCount}";
        }

        var raw = BuildRawMessage(result.Stdout, result.Stderr);
        if (raw.Contains("device offline", StringComparison.OrdinalIgnoreCase))
        {
            return "Устройство офлайн.";
        }

        return string.IsNullOrWhiteSpace(raw)
            ? "Не удалось получить список пакетов."
            : raw.Trim();
    }

    private static string TranslateUninstallMessage(AdbProcessResult result)
    {
        if (!result.Started)
        {
            return "adb.exe не найден.";
        }

        var raw = BuildRawMessage(result.Stdout, result.Stderr);
        if (result.ExitCode == 0 && raw.Contains("Success", StringComparison.OrdinalIgnoreCase))
        {
            return "Удалено.";
        }

        if (raw.Contains("DELETE_FAILED_INTERNAL_ERROR", StringComparison.OrdinalIgnoreCase))
        {
            return "Внутренняя ошибка удаления.";
        }

        if (raw.Contains("DELETE_FAILED_DEVICE_POLICY_MANAGER", StringComparison.OrdinalIgnoreCase))
        {
            return "Удаление запрещено политикой устройства.";
        }

        if (raw.Contains("Unknown package", StringComparison.OrdinalIgnoreCase) ||
            raw.Contains("not installed for", StringComparison.OrdinalIgnoreCase))
        {
            return "Пакет не установлен.";
        }

        if (raw.Contains("device offline", StringComparison.OrdinalIgnoreCase))
        {
            return "Устройство офлайн.";
        }

        if (string.IsNullOrWhiteSpace(raw))
        {
            return result.ExitCode == 0 ? "Удалено." : "Не удалось удалить пакет.";
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
