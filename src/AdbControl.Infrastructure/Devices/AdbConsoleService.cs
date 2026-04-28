using AdbControl.Application.Terminal;

namespace AdbControl.Infrastructure.Devices;

public sealed class AdbConsoleService : IAdbConsoleService
{
    private readonly AdbProcessRunner _adbProcessRunner;

    public AdbConsoleService(AdbProcessRunner adbProcessRunner)
    {
        _adbProcessRunner = adbProcessRunner;
    }

    public async Task<AdbConsoleCommandResult> ExecuteAsync(string rawCommand, CancellationToken cancellationToken = default)
    {
        var normalized = NormalizeCommand(rawCommand);
        if (!normalized.IsValid)
        {
            return new AdbConsoleCommandResult(
                normalized.CommandText,
                false,
                -3,
                string.Empty,
                normalized.ErrorMessage ?? "Команда не распознана.");
        }

        var result = await _adbProcessRunner.RunAsync(normalized.Arguments, cancellationToken);
        if (!result.Started)
        {
            return new AdbConsoleCommandResult(
                normalized.CommandText,
                false,
                -1,
                string.Empty,
                "adb.exe не найден. Добавь platform-tools в PATH.");
        }

        return new AdbConsoleCommandResult(
            normalized.CommandText,
            true,
            result.ExitCode,
            result.Stdout,
            result.Stderr);
    }

    private static NormalizedAdbCommand NormalizeCommand(string rawCommand)
    {
        var trimmed = rawCommand?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(trimmed))
        {
            return NormalizedAdbCommand.Invalid("adb", "Команда пуста.");
        }

        if (string.Equals(trimmed, "adb", StringComparison.OrdinalIgnoreCase))
        {
            return new NormalizedAdbCommand(string.Empty, "adb");
        }

        if (trimmed.StartsWith("adb ", StringComparison.OrdinalIgnoreCase))
        {
            var arguments = trimmed[4..].TrimStart();
            if (string.Equals(arguments, "shell", StringComparison.OrdinalIgnoreCase))
            {
                return NormalizedAdbCommand.Invalid(
                    "adb shell",
                    "Интерактивный shell здесь не поддерживается. Добавь команду после shell.");
            }

            return new NormalizedAdbCommand(arguments, $"adb {arguments}");
        }

        if (string.Equals(trimmed, "shell", StringComparison.OrdinalIgnoreCase))
        {
            return NormalizedAdbCommand.Invalid(
                "adb shell",
                "Интерактивный shell здесь не поддерживается. Добавь команду после shell.");
        }

        return new NormalizedAdbCommand(trimmed, $"adb {trimmed}");
    }

    private sealed record NormalizedAdbCommand(string Arguments, string CommandText, bool IsValid = true, string? ErrorMessage = null)
    {
        public static NormalizedAdbCommand Invalid(string commandText, string errorMessage)
        {
            return new NormalizedAdbCommand(string.Empty, commandText, false, errorMessage);
        }
    }
}
