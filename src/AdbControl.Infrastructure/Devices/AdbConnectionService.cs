using AdbControl.Application.Devices;

namespace AdbControl.Infrastructure.Devices;

public sealed class AdbConnectionService : IAdbConnectionService
{
    private readonly AdbProcessRunner _adbProcessRunner;

    public AdbConnectionService(AdbProcessRunner adbProcessRunner)
    {
        _adbProcessRunner = adbProcessRunner;
    }

    public async Task<AdbConnectResult> ConnectAsync(string endpoint, CancellationToken cancellationToken = default)
    {
        var connectResult = await _adbProcessRunner.RunAsync($"connect {endpoint}", cancellationToken);
        if (!connectResult.Started)
        {
            return new AdbConnectResult(endpoint, false, "adb.exe не найден. Добавь platform-tools в PATH.");
        }

        var rawMessage = BuildRawMessage(connectResult.Stdout, connectResult.Stderr);
        if (!IndicatesConnected(rawMessage))
        {
            return new AdbConnectResult(endpoint, false, TranslateFailureMessage(rawMessage));
        }

        var verifyResult = await _adbProcessRunner.RunAsync($"-s {endpoint} get-state", cancellationToken);
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
        var disconnectResult = await _adbProcessRunner.RunAsync($"disconnect {endpoint}", cancellationToken);
        if (!disconnectResult.Started)
        {
            return new AdbDisconnectResult(endpoint, false, "adb.exe не найден. Добавь platform-tools в PATH.");
        }

        var rawMessage = BuildRawMessage(disconnectResult.Stdout, disconnectResult.Stderr);
        var verifyResult = await _adbProcessRunner.RunAsync(
            $"-s {endpoint} get-state",
            cancellationToken,
            result => result.ExitCode != 0 && !IsExpectedMissingDevice(result, endpoint));
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

    public async Task<IReadOnlyList<string>> GetConnectedEndpointsAsync(CancellationToken cancellationToken = default)
    {
        var result = await _adbProcessRunner.RunAsync(
            "devices",
            cancellationToken,
            recordInJournal: false);

        if (!result.Started || result.ExitCode != 0)
        {
            return [];
        }

        return ParseConnectedEndpoints(result.Stdout);
    }

    public async Task<AdbPairResult> PairAsync(
        string endpoint,
        string pairingCode,
        CancellationToken cancellationToken = default)
    {
        var pairResult = await _adbProcessRunner.RunAsync(
            $"pair {endpoint} {pairingCode}",
            cancellationToken,
            journalArguments: $"pair {endpoint} ******");

        if (!pairResult.Started)
        {
            return new AdbPairResult(endpoint, false, "adb.exe не найден. Добавь platform-tools в PATH.");
        }

        var rawMessage = BuildRawMessage(pairResult.Stdout, pairResult.Stderr);
        return rawMessage.Contains("successfully paired", StringComparison.OrdinalIgnoreCase)
            ? new AdbPairResult(endpoint, true, "Сопряжено.")
            : new AdbPairResult(endpoint, false, TranslatePairFailureMessage(rawMessage));
    }

    public async Task<string?> GetDeviceModelAsync(string endpoint, CancellationToken cancellationToken = default)
    {
        var result = await _adbProcessRunner.RunAsync(
            $"-s {endpoint} shell getprop ro.product.model",
            cancellationToken,
            recordInJournal: false);

        if (!result.Started || result.ExitCode != 0)
        {
            return null;
        }

        var model = result.Stdout.Trim();
        return string.IsNullOrWhiteSpace(model) ? null : model;
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

    private static string TranslatePairFailureMessage(string rawMessage)
    {
        if (rawMessage.Contains("wrong password", StringComparison.OrdinalIgnoreCase) ||
            rawMessage.Contains("connection was dropped", StringComparison.OrdinalIgnoreCase))
        {
            return "Неверный код или устройство закрыло окно сопряжения.";
        }

        if (rawMessage.Contains("failed to connect", StringComparison.OrdinalIgnoreCase) ||
            rawMessage.Contains("unable to connect", StringComparison.OrdinalIgnoreCase))
        {
            return "Не удалось подключиться к порту сопряжения.";
        }

        return string.IsNullOrWhiteSpace(rawMessage)
            ? "Сопряжение не удалось."
            : "Сопряжение не удалось. Проверь адрес, порт и код.";
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

    private static bool IsExpectedMissingDevice(AdbProcessResult result, string endpoint)
    {
        var rawMessage = BuildRawMessage(result.Stdout, result.Stderr);
        return rawMessage.Contains($"device '{endpoint}' not found", StringComparison.OrdinalIgnoreCase) ||
               rawMessage.Contains("device not found", StringComparison.OrdinalIgnoreCase);
    }

    private static IReadOnlyList<string> ParseConnectedEndpoints(string stdout)
    {
        var endpoints = new List<string>();

        foreach (var rawLine in stdout.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
        {
            var line = rawLine.Trim();
            if (string.IsNullOrWhiteSpace(line) ||
                line.StartsWith("List of devices attached", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var parts = line.Split([' ', '\t'], StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 2 || !string.Equals(parts[1], "device", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var endpoint = parts[0].Trim();
            if (IsIpv4Endpoint(endpoint))
            {
                endpoints.Add(endpoint);
            }
        }

        return endpoints
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static bool IsIpv4Endpoint(string value)
    {
        var separatorIndex = value.LastIndexOf(':');
        if (separatorIndex <= 0 || separatorIndex == value.Length - 1)
        {
            return false;
        }

        var host = value[..separatorIndex];
        var port = value[(separatorIndex + 1)..];

        return IsValidIpv4(host) &&
               int.TryParse(port, out var parsedPort) &&
               parsedPort is > 0 and <= 65535;
    }

    private static bool IsValidIpv4(string value)
    {
        var octets = value.Split('.', StringSplitOptions.None);
        if (octets.Length != 4)
        {
            return false;
        }

        foreach (var octet in octets)
        {
            if (octet.Length == 0 ||
                octet.Length > 3 ||
                !int.TryParse(octet, out var part) ||
                part is < 0 or > 255)
            {
                return false;
            }
        }

        return true;
    }
}
