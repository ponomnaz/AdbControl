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

    public Task<DeviceActionBatchResult> ConfigureNetariumServerAsync(
        IReadOnlyList<TvDeviceProfile> devices,
        string serverEndpoint,
        CancellationToken cancellationToken = default)
    {
        var normalizedServerEndpoint = NormalizeServerEndpoint(serverEndpoint)
            ?? throw new ArgumentException("Server endpoint is required.", nameof(serverEndpoint));

        return RunAsync(
            devices,
            endpoint => $"-s {endpoint} shell am broadcast -a cs.netarium.config --es SERVER \"{normalizedServerEndpoint}\" {NetariumPackageName}",
            cancellationToken);
    }

    public async Task<CacheClearBatchResult> ClearNetariumCacheAsync(
        IReadOnlyList<TvDeviceProfile> devices,
        CancellationToken cancellationToken = default)
    {
        var successCount = 0;
        var relaunchedCount = 0;
        var freedBytes = 0L;

        foreach (var device in devices)
        {
            var target = ResolveTarget(device);
            if (target is null)
            {
                continue;
            }

            // 1. Погасить: иначе живое приложение перезапишет кэш из памяти.
            if (!await RunCommandAsync($"-s {target} shell am force-stop {NetariumPackageName}", cancellationToken))
            {
                continue;
            }

            // 2. Замерить, удалить и убедиться, что каталога больше нет.
            var clearResult = await _adbProcessRunner.RunAsync(
                $"-s {target} shell {BuildClearCacheScript()}",
                cancellationToken);

            if (!clearResult.Started || clearResult.ExitCode != 0)
            {
                continue;
            }

            if (!TryParseClearOutput(clearResult.Stdout, out var deviceFreedBytes))
            {
                continue;
            }

            freedBytes += deviceFreedBytes;
            successCount++;

            // 3. Запустить обратно.
            if (await TryRelaunchAsync(target, cancellationToken))
            {
                relaunchedCount++;
            }
        }

        return new CacheClearBatchResult(
            devices.Count,
            successCount,
            devices.Count - successCount,
            freedBytes,
            relaunchedCount);
    }

    /// <summary>
    /// Запуск приложения обратно. Компонент выясняется у самого устройства, а не задан
    /// в коде: Netarium — домашний экран, его активити объявлена с категорией HOME,
    /// поэтому обычный <c>monkey -c LAUNCHER</c> её не находит.
    /// </summary>
    private async Task<bool> TryRelaunchAsync(string target, CancellationToken cancellationToken)
    {
        string[] categories =
        [
            "android.intent.category.LAUNCHER",
            "android.intent.category.HOME"
        ];

        foreach (var category in categories)
        {
            var resolveResult = await _adbProcessRunner.RunAsync(
                $"-s {target} shell cmd package resolve-activity --brief -a android.intent.action.MAIN -c {category} {NetariumPackageName}",
                cancellationToken,
                recordInJournal: false);

            if (!resolveResult.Started || resolveResult.ExitCode != 0)
            {
                continue;
            }

            if (ParseResolvedComponent(resolveResult.Stdout) is not { } component)
            {
                continue;
            }

            if (await RunCommandAsync($"-s {target} shell am start -n {component}", cancellationToken))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// <c>resolve-activity --brief</c> печатает служебные строки, а компонент — последней.
    /// Когда активити нет, выводится «No activity found».
    /// </summary>
    private static string? ParseResolvedComponent(string stdout)
    {
        foreach (var rawLine in stdout.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries).Reverse())
        {
            var line = rawLine.Trim();
            if (line.StartsWith($"{NetariumPackageName}/", StringComparison.Ordinal))
            {
                return line;
            }
        }

        return null;
    }

    /// <summary>
    /// Путь берётся через <c>$EXTERNAL_STORAGE</c>: на устройствах с несколькими
    /// пользователями это не <c>/sdcard</c>. Каталог намеренно не пересоздаётся —
    /// созданный от имени shell, он остался бы недоступен самому приложению.
    /// </summary>
    private static string BuildClearCacheScript()
    {
        const string cachePath = $"$EXTERNAL_STORAGE/Android/data/{NetariumPackageName}/files/.cache";

        return $"\"echo SIZE=$(du -sk {cachePath} 2>/dev/null | cut -f1); " +
               $"rm -rf {cachePath}; " +
               $"echo LEFT=$(ls -d {cachePath} 2>/dev/null)\"";
    }

    /// <summary>
    /// <c>rm -rf</c> возвращает ноль всегда, поэтому успех определяется по тому,
    /// исчез ли каталог, а не по коду возврата.
    /// </summary>
    private static bool TryParseClearOutput(string stdout, out long freedBytes)
    {
        freedBytes = 0;
        var isCleared = false;

        foreach (var rawLine in stdout.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
        {
            var line = rawLine.Trim();

            if (line.StartsWith("SIZE=", StringComparison.Ordinal) &&
                long.TryParse(line[5..].Trim(), out var sizeKilobytes))
            {
                freedBytes = sizeKilobytes * 1024;
            }
            else if (line.StartsWith("LEFT=", StringComparison.Ordinal))
            {
                // Пусто — каталога больше нет, значит удаление состоялось.
                isCleared = line[5..].Trim().Length == 0;
            }
        }

        return isCleared;
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
            var target = ResolveTarget(device);
            if (target is null)
            {
                continue;
            }

            var isSuccess = await RunCommandAsync(argumentsFactory(target), cancellationToken);
            if (isSuccess)
            {
                successCount++;
                successfulEndpoints.Add(target);
            }
        }

        return new DeviceActionBatchResult(
            devices.Count,
            successCount,
            devices.Count - successCount,
            successfulEndpoints);
    }

    private static string? ResolveTarget(TvDeviceProfile device)
    {
        return AdbTarget.Resolve(device);
    }

    private async Task<bool> RunCommandAsync(string arguments, CancellationToken cancellationToken)
    {
        var result = await _adbProcessRunner.RunAsync(arguments, cancellationToken);
        return result.Started && result.ExitCode == 0;
    }

    private static string? NormalizeServerEndpoint(string? serverEndpoint)
    {
        var normalizedEndpoint = NetariumServerEndpointCatalog.NormalizeEndpoint(serverEndpoint);
        return string.IsNullOrWhiteSpace(normalizedEndpoint)
            ? null
            : $"tcp://{normalizedEndpoint}";
    }
}
