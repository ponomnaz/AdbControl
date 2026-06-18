using System.ComponentModel;
using System.Diagnostics;
using AdbControl.Application.Diagnostics;
using AdbControl.Application.Remote;

namespace AdbControl.Infrastructure.Devices;

public sealed class AdbRemoteControlService : IRemoteControlService
{
    private readonly AdbProcessRunner _adbProcessRunner;
    private readonly CommandTraceJournal _commandTraceJournal;
    private readonly object _sessionLock = new();
    private Process? _sessionProcess;
    private string? _sessionTargetId;

    public AdbRemoteControlService(AdbProcessRunner adbProcessRunner, CommandTraceJournal commandTraceJournal)
    {
        _adbProcessRunner = adbProcessRunner;
        _commandTraceJournal = commandTraceJournal;
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

                // Drain output pipes so the shell never blocks on a full buffer.
                _ = DrainStreamAsync(process.StandardOutput);
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
        var command = $"input keyevent {keyCode}";
        var stopwatch = Stopwatch.StartNew();

        if (TryWriteToSession(targetId, command))
        {
            stopwatch.Stop();
            await _commandTraceJournal.RecordAsync(
                new CommandTraceEntry(
                    Guid.NewGuid(),
                    DateTimeOffset.Now,
                    $"[session] -s {targetId} shell {command}",
                    $"{stopwatch.Elapsed.TotalMilliseconds:F1} ms (запись в персистентную сессию)",
                    string.Empty,
                    0,
                    false),
                cancellationToken);
            return true;
        }

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
                $"{stopwatch.Elapsed.TotalMilliseconds:F1} ms (новый процесс adb, сессия недоступна)\n{result.Stdout}",
                result.Stderr,
                result.ExitCode,
                !result.Started || result.ExitCode != 0),
            cancellationToken);

        return result.Started && result.ExitCode == 0;
    }

    private bool TryWriteToSession(string targetId, string command)
    {
        lock (_sessionLock)
        {
            if (_sessionProcess is null || _sessionProcess.HasExited ||
                !string.Equals(_sessionTargetId, targetId, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            try
            {
                _sessionProcess.StandardInput.WriteLine(command);
                _sessionProcess.StandardInput.Flush();
                return true;
            }
            catch
            {
                return false;
            }
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
