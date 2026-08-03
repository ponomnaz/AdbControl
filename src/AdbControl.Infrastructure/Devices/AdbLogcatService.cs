using System.ComponentModel;
using System.Diagnostics;
using AdbControl.Application.Diagnostics;
using AdbControl.Application.Logcat;
using AdbControl.Core.Devices;

namespace AdbControl.Infrastructure.Devices;

public sealed class AdbLogcatService : IDeviceLogcatService
{
    private readonly AdbProcessRunner _adbProcessRunner;
    private readonly CommandTraceJournal _commandTraceJournal;

    public AdbLogcatService(AdbProcessRunner adbProcessRunner, CommandTraceJournal commandTraceJournal)
    {
        _adbProcessRunner = adbProcessRunner;
        _commandTraceJournal = commandTraceJournal;
    }

    public async Task<DeviceLogcatSessionStartResult> StartSessionAsync(
        TvDeviceProfile device,
        string filterArguments,
        Action<LogcatOutputLine> onLine,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(device);
        ArgumentNullException.ThrowIfNull(onLine);

        var targetId = GetTargetId(device);
        if (string.IsNullOrWhiteSpace(targetId))
        {
            return DeviceLogcatSessionStartResult.Failure("У устройства нет ADB-идентификатора.");
        }

        var trimmedFilter = filterArguments?.Trim() ?? string.Empty;
        var arguments = string.IsNullOrWhiteSpace(trimmedFilter)
            ? $"-s {targetId} logcat"
            : $"-s {targetId} logcat {trimmedFilter}";
        var commandText = $"adb {arguments}";

        var process = new Process
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
            await _commandTraceJournal.RecordAsync(
                new CommandTraceEntry(
                    Guid.NewGuid(),
                    DateTimeOffset.Now,
                    commandText,
                    string.Empty,
                    "adb.exe не найден. Добавь platform-tools в PATH.",
                    -1,
                    true),
                cancellationToken);

            process.Dispose();
            return DeviceLogcatSessionStartResult.Failure("adb.exe не найден.");
        }

        await _commandTraceJournal.RecordAsync(
            new CommandTraceEntry(
                Guid.NewGuid(),
                DateTimeOffset.Now,
                commandText,
                "Сессия logcat запущена.",
                string.Empty,
                0,
                false),
            cancellationToken);

        var session = new AdbLogcatSession(process, onLine);
        return DeviceLogcatSessionStartResult.Success(session);
    }

    public async Task<DeviceLogcatClearResult> ClearBufferAsync(
        TvDeviceProfile device,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(device);

        var targetId = GetTargetId(device);
        if (string.IsNullOrWhiteSpace(targetId))
        {
            return DeviceLogcatClearResult.Failure("У устройства нет ADB-идентификатора.");
        }

        var result = await _adbProcessRunner.RunAsync($"-s {targetId} logcat -c", cancellationToken);
        if (!result.Started)
        {
            return DeviceLogcatClearResult.Failure("adb.exe не найден.");
        }

        if (result.ExitCode == 0)
        {
            return DeviceLogcatClearResult.Success("Буфер очищен.");
        }

        var errorText = string.Join(" ", new[] { result.Stdout, result.Stderr }
            .Where(static value => !string.IsNullOrWhiteSpace(value))
            .Select(static value => value.Trim()));

        return DeviceLogcatClearResult.Failure(string.IsNullOrWhiteSpace(errorText) ? "Не удалось очистить буфер." : errorText);
    }

    public async Task<int?> GetProcessIdAsync(
        TvDeviceProfile device,
        string packageName,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(device);

        var targetId = GetTargetId(device);
        if (string.IsNullOrWhiteSpace(targetId))
        {
            return null;
        }

        var result = await _adbProcessRunner.RunAsync(
            $"-s {targetId} shell pidof {packageName}",
            cancellationToken,
            recordInJournal: false);

        if (!result.Started)
        {
            return null;
        }

        // pidof возвращает ненулевой код, когда процесса нет, — это не ошибка.
        var firstPid = result.Stdout
            .Split([' ', '\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
            .FirstOrDefault();

        return int.TryParse(firstPid, out var processId) ? processId : null;
    }

    private static string GetTargetId(TvDeviceProfile device)
    {
        return string.IsNullOrWhiteSpace(device.NetworkEndpoint)
            ? device.Id
            : device.NetworkEndpoint;
    }

    private sealed class AdbLogcatSession : IDeviceLogcatSession
    {
        private readonly Process _process;
        private readonly Action<LogcatOutputLine> _onLine;
        private readonly Task _stdoutTask;
        private readonly Task _stderrTask;
        private int _isStopped;

        public AdbLogcatSession(Process process, Action<LogcatOutputLine> onLine)
        {
            _process = process;
            _onLine = onLine;
            _stdoutTask = PumpAsync(_process.StandardOutput, isError: false);
            _stderrTask = PumpAsync(_process.StandardError, isError: true);
        }

        public async Task StopAsync(CancellationToken cancellationToken = default)
        {
            if (Interlocked.Exchange(ref _isStopped, 1) != 0)
            {
                return;
            }

            try
            {
                if (!_process.HasExited)
                {
                    _process.Kill(entireProcessTree: true);
                }
            }
            catch
            {
                // Ignore best-effort process termination failures.
            }

            try
            {
                await _process.WaitForExitAsync(cancellationToken);
            }
            catch
            {
                // Process may already be gone.
            }

            await Task.WhenAll(_stdoutTask, _stderrTask);
            _process.Dispose();
        }

        public async ValueTask DisposeAsync()
        {
            await StopAsync();
        }

        private async Task PumpAsync(StreamReader reader, bool isError)
        {
            try
            {
                while (true)
                {
                    var line = await reader.ReadLineAsync();
                    if (line is null)
                    {
                        break;
                    }

                    _onLine(new LogcatOutputLine(line, isError));
                }
            }
            catch
            {
                // Ignore stream failures while stopping.
            }
        }
    }
}
