using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using AdbControl.Application.Diagnostics;
using AdbControl.Application.Remote;

namespace AdbControl.Infrastructure.Devices;

public sealed class AdbRemoteControlService : IRemoteControlService
{
    /// <summary>
    /// Маркер завершения пачки. Утилита <c>input</c> на устройстве — java-программа,
    /// поднимающая виртуальную машину при каждом запуске: на ТВ-процессоре это ~1.2 с
    /// независимо от числа кейкодов. Поэтому нажатия, пришедшие пока устройство занято,
    /// уходят следующей командой одним вызовом <c>input keyevent K1 K2 ...</c>.
    /// </summary>
    private const string BatchMarker = "__adbcontrol_keys_done__";

    /// <summary>Страховка на случай, если маркер не вернётся: иначе очередь встанет навсегда.</summary>
    private static readonly TimeSpan BatchWatchdog = TimeSpan.FromSeconds(5);

    private readonly AdbProcessRunner _adbProcessRunner;
    private readonly CommandTraceJournal _commandTraceJournal;
    private readonly object _sessionLock = new();
    private readonly List<int> _pendingKeys = [];
    private Process? _sessionProcess;
    private string? _sessionTargetId;
    private bool _isBatchInFlight;
    private DateTimeOffset _batchStartedAt;
    private Stopwatch? _batchStopwatch;
    private int[] _batchKeys = [];

    public AdbRemoteControlService(AdbProcessRunner adbProcessRunner, CommandTraceJournal commandTraceJournal)
    {
        _adbProcessRunner = adbProcessRunner;
        _commandTraceJournal = commandTraceJournal;
    }

    /// <summary>
    /// Разбирает вывод <c>wm size</c> и <c>wm density</c>. Обе команды печатают строку
    /// «Physical …», а «Override …» появляется только когда значение переопределено.
    /// </summary>
    public async Task<DeviceDisplayState?> ReadDisplayAsync(string targetId, CancellationToken cancellationToken = default)
    {
        var size = await _adbProcessRunner.RunAsync(
            $"-s {targetId} shell wm size",
            cancellationToken,
            recordInJournal: false);

        // Ненулевой код возврата — устройства нет или оно недоступно. Возвращать
        // «состояние» из пустых значений нельзя: вкладка примет это за прочитанный экран.
        if (!size.Started || size.ExitCode != 0)
        {
            return null;
        }

        var density = await _adbProcessRunner.RunAsync(
            $"-s {targetId} shell wm density",
            cancellationToken,
            recordInJournal: false);

        return new DeviceDisplayState(
            ExtractSize(size.Stdout, "Physical size"),
            ExtractSize(size.Stdout, "Override size"),
            ExtractDensity(density.Stdout, "Physical density"),
            ExtractDensity(density.Stdout, "Override density"));
    }

    public async Task<bool> ApplySizeAsync(string targetId, string? size, CancellationToken cancellationToken = default)
    {
        var argument = string.IsNullOrWhiteSpace(size) ? "reset" : size.Trim();

        var result = await _adbProcessRunner.RunAsync(
            $"-s {targetId} shell wm size {argument}",
            cancellationToken);

        return result.Started && result.ExitCode == 0;
    }

    public async Task<bool> ApplyDensityAsync(string targetId, int? density, CancellationToken cancellationToken = default)
    {
        var argument = density is { } value ? value.ToString() : "reset";

        var result = await _adbProcessRunner.RunAsync(
            $"-s {targetId} shell wm density {argument}",
            cancellationToken);

        return result.Started && result.ExitCode == 0;
    }

    private static string? ExtractSize(string output, string label)
    {
        var value = ExtractLabelled(output, label);

        // Ожидаем «1920x1080»; всё остальное считаем нечитаемым и не показываем.
        return value is not null && value.Contains('x', StringComparison.OrdinalIgnoreCase)
            ? value
            : null;
    }

    private static int? ExtractDensity(string output, string label)
    {
        return int.TryParse(ExtractLabelled(output, label), out var value) ? value : null;
    }

    private static string? ExtractLabelled(string output, string label)
    {
        if (string.IsNullOrWhiteSpace(output))
        {
            return null;
        }

        foreach (var line in output.Split('\n'))
        {
            var trimmed = line.Trim();

            if (!trimmed.StartsWith(label, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var separator = trimmed.IndexOf(':');
            if (separator < 0)
            {
                continue;
            }

            var value = trimmed[(separator + 1)..].Trim();
            return value.Length == 0 ? null : value;
        }

        return null;
    }

    public Task StartSessionAsync(string targetId, CancellationToken cancellationToken = default)
    {
        lock (_sessionLock)
        {
            if (_sessionProcess is not null && !_sessionProcess.HasExited &&
                string.Equals(_sessionTargetId, targetId, StringComparison.OrdinalIgnoreCase))
            {
                return Task.CompletedTask;
            }

            StopSessionCore();

            try
            {
                var process = new Process
                {
                    StartInfo = new ProcessStartInfo
                    {
                        FileName = "adb",
                        Arguments = $"-s {targetId} shell",
                        RedirectStandardInput = true,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        UseShellExecute = false,
                        CreateNoWindow = true
                    },
                    EnableRaisingEvents = true
                };

                process.Exited += OnSessionExited;
                process.Start();

                // stdout читаем ради маркера завершения, stderr просто осушаем,
                // чтобы оболочка не встала на переполненном буфере.
                _ = ReadSessionOutputAsync(process.StandardOutput);
                _ = DrainStreamAsync(process.StandardError);

                _sessionProcess = process;
                _sessionTargetId = targetId;
            }
            catch (Win32Exception)
            {
                _sessionProcess = null;
                _sessionTargetId = null;
            }
        }

        return Task.CompletedTask;
    }

    public void StopSession()
    {
        lock (_sessionLock)
        {
            StopSessionCore();
        }
    }

    public async Task<bool> SendKeyEventAsync(string targetId, int keyCode, CancellationToken cancellationToken = default)
    {
        if (TryEnqueueForSession(targetId, keyCode))
        {
            return true;
        }

        var command = $"input keyevent {keyCode}";
        var stopwatch = Stopwatch.StartNew();

        var result = await _adbProcessRunner.RunAsync(
            $"-s {targetId} shell {command}",
            cancellationToken,
            recordInJournal: false);

        stopwatch.Stop();

        await _commandTraceJournal.RecordAsync(
            new CommandTraceEntry(
                Guid.NewGuid(),
                DateTimeOffset.Now,
                $"[one-shot] -s {targetId} shell {command}",
                $"новый процесс adb, сессия недоступна\n{result.Stdout}",
                result.Stderr,
                result.ExitCode,
                !result.Started || result.ExitCode != 0,
                (int)stopwatch.ElapsedMilliseconds),
            cancellationToken);

        return result.Started && result.ExitCode == 0;
    }

    /// <summary>
    /// Кладёт нажатие в очередь сессии. Если устройство сейчас не занято — отправляет сразу,
    /// иначе нажатие уедет вместе с остальными следующей пачкой.
    /// </summary>
    private bool TryEnqueueForSession(string targetId, int keyCode)
    {
        lock (_sessionLock)
        {
            if (_sessionProcess is null || _sessionProcess.HasExited ||
                !string.Equals(_sessionTargetId, targetId, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            _pendingKeys.Add(keyCode);

            if (_isBatchInFlight && DateTimeOffset.UtcNow - _batchStartedAt < BatchWatchdog)
            {
                return true;
            }

            return FlushPendingLocked();
        }
    }

    /// <summary>Вызывается под удержанным <see cref="_sessionLock"/>.</summary>
    private bool FlushPendingLocked()
    {
        if (_pendingKeys.Count == 0 || _sessionProcess is null || _sessionProcess.HasExited)
        {
            return _pendingKeys.Count == 0;
        }

        var keys = _pendingKeys.ToArray();
        _pendingKeys.Clear();

        try
        {
            _sessionProcess.StandardInput.WriteLine(
                $"input keyevent {string.Join(' ', keys)}; echo {BatchMarker}");
            _sessionProcess.StandardInput.Flush();
        }
        catch
        {
            _isBatchInFlight = false;
            return false;
        }

        _isBatchInFlight = true;
        _batchStartedAt = DateTimeOffset.UtcNow;
        _batchStopwatch = Stopwatch.StartNew();
        _batchKeys = keys;
        return true;
    }

    private void CompleteBatch()
    {
        int[] keys;
        TimeSpan elapsed;

        lock (_sessionLock)
        {
            if (!_isBatchInFlight)
            {
                return;
            }

            _isBatchInFlight = false;
            keys = _batchKeys;
            elapsed = _batchStopwatch?.Elapsed ?? TimeSpan.Zero;
            _batchKeys = [];
            _batchStopwatch = null;

            // Всё, что нажали, пока устройство было занято, уходит одной командой.
            FlushPendingLocked();
        }

        _ = RecordBatchAsync(keys, elapsed);
    }

    private async Task RecordBatchAsync(int[] keys, TimeSpan elapsed)
    {
        if (keys.Length == 0)
        {
            return;
        }

        try
        {
            await _commandTraceJournal.RecordAsync(
                new CommandTraceEntry(
                    Guid.NewGuid(),
                    DateTimeOffset.Now,
                    $"[session] input keyevent {string.Join(' ', keys)}",
                    $"нажатий в пачке: {keys.Length}",
                    string.Empty,
                    0,
                    false,
                    (int)elapsed.TotalMilliseconds),
                CancellationToken.None);
        }
        catch
        {
            // Журнал не должен мешать вводу.
        }
    }

    private async Task ReadSessionOutputAsync(StreamReader reader)
    {
        try
        {
            while (await reader.ReadLineAsync() is { } line)
            {
                if (line.Contains(BatchMarker, StringComparison.Ordinal))
                {
                    CompleteBatch();
                }
            }
        }
        catch
        {
            // Поток закрывается вместе с процессом.
        }
    }

    private void OnSessionExited(object? sender, EventArgs e)
    {
        lock (_sessionLock)
        {
            if (ReferenceEquals(_sessionProcess, sender))
            {
                _sessionProcess = null;
                _sessionTargetId = null;
            }
        }
    }

    private void StopSessionCore()
    {
        if (_sessionProcess is null)
        {
            return;
        }

        var process = _sessionProcess;
        _sessionProcess = null;
        _sessionTargetId = null;

        _pendingKeys.Clear();
        _isBatchInFlight = false;
        _batchStopwatch = null;
        _batchKeys = [];

        process.Exited -= OnSessionExited;

        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch
        {
            // Ignore - process may have already exited.
        }

        process.Dispose();
    }

    private static async Task DrainStreamAsync(StreamReader reader)
    {
        var buffer = new char[256];
        try
        {
            while (await reader.ReadAsync(buffer) > 0)
            {
                // Discard - the shell's output isn't needed.
            }
        }
        catch
        {
            // Stream closed when the process exits.
        }
    }
}
