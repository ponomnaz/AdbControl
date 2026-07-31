using System.Globalization;
using AdbControl.Application.Devices;

namespace AdbControl.Infrastructure.Devices;

/// <summary>
/// Обёртка над <c>adb mdns services</c>. Свой mDNS-клиент не нужен: platform-tools 30+
/// поднимают демон обнаружения сами и отдают готовый список со случайными портами.
/// </summary>
public sealed class AdbMdnsDiscoveryService : IMdnsDiscoveryService
{
    private readonly AdbProcessRunner _adbProcessRunner;

    public AdbMdnsDiscoveryService(AdbProcessRunner adbProcessRunner)
    {
        _adbProcessRunner = adbProcessRunner;
    }

    public async Task<IReadOnlyList<MdnsAdbService>> DiscoverAsync(CancellationToken cancellationToken = default)
    {
        var result = await _adbProcessRunner.RunAsync(
            "mdns services",
            cancellationToken,
            recordInJournal: false);

        if (!result.Started || result.ExitCode != 0)
        {
            return [];
        }

        return Parse(result.Stdout);
    }

    /// <summary>Разбор вывода <c>adb mdns services</c>. Публичный, чтобы поддаваться проверке отдельно от adb.</summary>
    public static IReadOnlyList<MdnsAdbService> Parse(string stdout)
    {
        var services = new List<MdnsAdbService>();

        foreach (var rawLine in stdout.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
        {
            var line = rawLine.Trim();
            if (line.Length == 0 ||
                line.StartsWith("List of discovered mdns services", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            // Имя экземпляра может содержать пробелы, поэтому опираемся на хвост строки:
            // последняя колонка — адрес:порт, предпоследняя — тип службы.
            var parts = line.Split([' ', '\t'], StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 3)
            {
                continue;
            }

            if (!TryParseEndpoint(parts[^1], out var host, out var port))
            {
                continue;
            }

            var kind = ParseKind(parts[^2]);
            if (kind == MdnsServiceKind.Unknown)
            {
                continue;
            }

            services.Add(new MdnsAdbService(
                string.Join(' ', parts[..^2]),
                kind,
                host,
                port));
        }

        return services;
    }

    private static MdnsServiceKind ParseKind(string serviceType)
    {
        if (serviceType.Contains("_adb-tls-connect", StringComparison.OrdinalIgnoreCase))
        {
            return MdnsServiceKind.Connect;
        }

        if (serviceType.Contains("_adb-tls-pairing", StringComparison.OrdinalIgnoreCase))
        {
            return MdnsServiceKind.Pairing;
        }

        return serviceType.StartsWith("_adb.", StringComparison.OrdinalIgnoreCase)
            ? MdnsServiceKind.Legacy
            : MdnsServiceKind.Unknown;
    }

    private static bool TryParseEndpoint(string value, out string host, out int port)
    {
        host = string.Empty;
        port = 0;

        var separatorIndex = value.LastIndexOf(':');
        if (separatorIndex <= 0 || separatorIndex == value.Length - 1)
        {
            return false;
        }

        host = value[..separatorIndex].Trim('[', ']');

        return host.Length > 0 &&
               int.TryParse(value[(separatorIndex + 1)..], NumberStyles.None, CultureInfo.InvariantCulture, out port) &&
               port is > 0 and <= 65535;
    }
}
