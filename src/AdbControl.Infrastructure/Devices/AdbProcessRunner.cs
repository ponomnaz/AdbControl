using System.ComponentModel;
using System.Diagnostics;
using AdbControl.Application.Diagnostics;

namespace AdbControl.Infrastructure.Devices;

public sealed class AdbProcessRunner
{
    private const int CanceledExitCode = -2;

    private readonly CommandTraceJournal _commandTraceJournal;

    public AdbProcessRunner(CommandTraceJournal commandTraceJournal)
    {
        _commandTraceJournal = commandTraceJournal;
    }

    internal async Task<AdbProcessResult> RunAsync(
        string arguments,
        CancellationToken cancellationToken,
        Func<AdbProcessResult, bool>? isErrorEvaluator = null,
        bool recordInJournal = true)
    {
        var commandText = string.IsNullOrWhiteSpace(arguments)
            ? "adb"
            : $"adb {arguments}";

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

            return AdbProcessResult.NotStarted;
        }

        using var cancellationRegistration = cancellationToken.Register(() =>
        {
            try
            {
                if (!process.HasExited)
                {
                    process.Kill(entireProcessTree: true);
                }
            }
            catch
            {
                // Ignore kill failures during cancellation.
            }
        });

        try
        {
            var stdoutTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
            var stderrTask = process.StandardError.ReadToEndAsync(cancellationToken);

            await process.WaitForExitAsync(cancellationToken);

            var stdout = await stdoutTask;
            var stderr = await stderrTask;
            var result = new AdbProcessResult(true, process.ExitCode, stdout, stderr);
            var isError = isErrorEvaluator?.Invoke(result) ?? result.ExitCode != 0;

            if (recordInJournal)
            {
                await _commandTraceJournal.RecordAsync(
                    new CommandTraceEntry(
                        Guid.NewGuid(),
                        DateTimeOffset.Now,
                        commandText,
                        stdout,
                        stderr,
                        process.ExitCode,
                        isError),
                    cancellationToken);
            }

            return result;
        }
        catch (OperationCanceledException)
        {
            if (recordInJournal)
            {
                await _commandTraceJournal.RecordAsync(
                    new CommandTraceEntry(
                        Guid.NewGuid(),
                        DateTimeOffset.Now,
                        commandText,
                        string.Empty,
                        "Операция отменена.",
                        CanceledExitCode,
                        true),
                    CancellationToken.None);
            }

            return new AdbProcessResult(true, CanceledExitCode, string.Empty, "Операция отменена.");
        }
    }
}

internal sealed record AdbProcessResult(bool Started, int ExitCode, string Stdout, string Stderr)
{
    public static AdbProcessResult NotStarted { get; } = new(false, -1, string.Empty, string.Empty);
}
