using System.ComponentModel;
using System.Diagnostics;
using AdbControl.Application.Devices;

namespace AdbControl.Infrastructure.Devices;

public sealed class AdbConnectionService : IAdbConnectionService
{
    public async Task<AdbConnectResult> ConnectAsync(string endpoint, CancellationToken cancellationToken = default)
    {
        var connectResult = await RunAdbCommandAsync($"connect {endpoint}", cancellationToken);
        if (!connectResult.Started)
        {
            return new AdbConnectResult(endpoint, false, "adb.exe не найден. Добавь platform-tools в PATH.");
        }

        var rawMessage = BuildRawMessage(connectResult.Stdout, connectResult.Stderr);
        if (!IndicatesConnected(rawMessage))
        {
            return new AdbConnectResult(endpoint, false, TranslateFailureMessage(rawMessage));
        }

        var verifyResult = await RunAdbCommandAsync($"-s {endpoint} get-state", cancellationToken);
        if (!verifyResult.Started)
        {
            return new AdbConnectResult(endpoint, false, "adb.exe не найден. Добавь platform-tools в PATH.");
        }

        var isVerified = verifyResult.ExitCode == 0 &&
                         string.Equals(verifyResult.Stdout.Trim(), "device", StringComparison.OrdinalIgnoreCase);

        return isVerified
            ? new AdbConnectResult(endpoint, true, TranslateSuccessMessage(rawMessage))
            : new AdbConnectResult(endpoint, false, "Не удалось подтвердить подключение.");
    }

    public async Task<AdbDisconnectResult> DisconnectAsync(string endpoint, CancellationToken cancellationToken = default)
    {
        var disconnectResult = await RunAdbCommandAsync($"disconnect {endpoint}", cancellationToken);
        if (!disconnectResult.Started)
        {
            return new AdbDisconnectResult(endpoint, false, "adb.exe не найден. Добавь platform-tools в PATH.");
        }

        var rawMessage = BuildRawMessage(disconnectResult.Stdout, disconnectResult.Stderr);
        var verifyResult = await RunAdbCommandAsync($"-s {endpoint} get-state", cancellationToken);
        if (!verifyResult.Started)
        {
            return new AdbDisconnectResult(endpoint, false, "adb.exe не найден. Добавь platform-tools в PATH.");
        }

        var isDisconnected = verifyResult.ExitCode != 0 ||
                             !string.Equals(verifyResult.Stdout.Trim(), "device", StringComparison.OrdinalIgnoreCase);

        return isDisconnected
            ? new AdbDisconnectResult(endpoint, true, TranslateDisconnectSuccessMessage(rawMessage))
            : new AdbDisconnectResult(endpoint, false, "Не удалось отключить устройство.");
    }

    private static bool IndicatesConnected(string rawMessage)
    {
        return rawMessage.Contains("connected to", StringComparison.OrdinalIgnoreCase) ||
               rawMessage.Contains("already connected", StringComparison.OrdinalIgnoreCase);
    }

    private static string BuildRawMessage(string stdout, string stderr)
    {
        return string.Join(" ", new[] { stdout, stderr }
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Select(x => x.Trim()));
    }

    private static string TranslateSuccessMessage(string rawMessage)
    {
        if (rawMessage.Contains("already connected", StringComparison.OrdinalIgnoreCase))
        {
            return "Уже подключено.";
        }

        return "Подключено.";
    }

    private static string TranslateFailureMessage(string rawMessage)
    {
        if (string.IsNullOrWhiteSpace(rawMessage))
        {
            return "Не удалось подключиться.";
        }

        if (rawMessage.Contains("failed to connect", StringComparison.OrdinalIgnoreCase))
        {
            return "Не удалось подключиться.";
        }

        if (rawMessage.Contains("connection refused", StringComparison.OrdinalIgnoreCase))
        {
            return "Подключение отклонено.";
        }

        if (rawMessage.Contains("cannot resolve host", StringComparison.OrdinalIgnoreCase))
        {
            return "Неверный адрес.";
        }

        return "Команда adb завершилась с ошибкой.";
    }

    private static string TranslateDisconnectSuccessMessage(string rawMessage)
    {
        if (rawMessage.Contains("no such device", StringComparison.OrdinalIgnoreCase))
        {
            return "Уже отключено.";
        }

        if (rawMessage.Contains("disconnected", StringComparison.OrdinalIgnoreCase))
        {
            return "Отключено.";
        }

        return "Отключено.";
    }

    private static async Task<AdbProcessResult> RunAdbCommandAsync(string arguments, CancellationToken cancellationToken)
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
            return AdbProcessResult.NotStarted;
        }

        var stdoutTask = process.StandardOutput.ReadToEndAsync();
        var stderrTask = process.StandardError.ReadToEndAsync();

        await process.WaitForExitAsync(cancellationToken);

        return new AdbProcessResult(
            true,
            process.ExitCode,
            await stdoutTask,
            await stderrTask);
    }
}

internal sealed record AdbProcessResult(bool Started, int ExitCode, string Stdout, string Stderr)
{
    public static AdbProcessResult NotStarted { get; } = new(false, -1, string.Empty, string.Empty);
}
