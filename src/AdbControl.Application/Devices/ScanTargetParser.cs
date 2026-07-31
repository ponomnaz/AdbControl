using System.Globalization;
using System.Net;

namespace AdbControl.Application.Devices;

/// <summary>
/// Разбирает пользовательскую строку целей сканирования. Поддерживаются, через запятую:
/// 212.59.101.0/24, 212.59.101.1-212.59.101.254, 212.59.101.1-254,
/// 212.59.101.75, 212.59.101.75:5555, tv.example.com, tv.example.com:5555.
/// </summary>
public static class ScanTargetParser
{
    public static bool TryParse(string? input, out IReadOnlyList<ScanTargetSpec> targets, out string? error)
    {
        targets = [];
        error = null;

        if (string.IsNullOrWhiteSpace(input))
        {
            return true;
        }

        var parsed = new List<ScanTargetSpec>();

        foreach (var token in input.Split(
                     [',', ';', '\r', '\n'],
                     StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (!TryParseToken(token, out var target, out error))
            {
                targets = [];
                return false;
            }

            parsed.Add(target);
        }

        targets = parsed;
        return true;
    }

    private static bool TryParseToken(string token, out ScanTargetSpec target, out string? error)
    {
        target = new AutoLocalScanTarget();
        error = null;

        if (token.Contains('/'))
        {
            return TryParseCidr(token, out target, out error);
        }

        // Диапазон — только если левая граница действительно IPv4: иначе это DNS-имя с дефисом.
        var dashIndex = token.IndexOf('-');
        if (dashIndex > 0 && TryParseIPv4(token[..dashIndex].Trim(), out _))
        {
            return TryParseRange(token, out target, out error);
        }

        var colonCount = token.Count(static character => character == ':');
        if (colonCount > 1)
        {
            error = $"IPv6 пока не поддерживается: {token}";
            return false;
        }

        if (colonCount == 1)
        {
            var separatorIndex = token.IndexOf(':');
            var host = token[..separatorIndex].Trim();
            if (!TryParsePort(token[(separatorIndex + 1)..], out var port))
            {
                error = $"Неверный порт: {token}";
                return false;
            }

            if (!IsPlausibleHost(host))
            {
                error = $"Неверный адрес: {token}";
                return false;
            }

            target = new EndpointScanTarget(host, port);
            return true;
        }

        if (!IsPlausibleHost(token))
        {
            error = $"Неверный адрес: {token}";
            return false;
        }

        target = new HostScanTarget(token);
        return true;
    }

    private static bool TryParseCidr(string token, out ScanTargetSpec target, out string? error)
    {
        target = new AutoLocalScanTarget();
        error = null;

        var separatorIndex = token.IndexOf('/');
        var addressPart = token[..separatorIndex].Trim();
        var prefixPart = token[(separatorIndex + 1)..].Trim();

        if (!TryParseIPv4(addressPart, out var address) ||
            !int.TryParse(prefixPart, NumberStyles.None, CultureInfo.InvariantCulture, out var prefixLength) ||
            prefixLength is < 0 or > 32)
        {
            error = $"Неверная подсеть: {token}";
            return false;
        }

        target = new CidrScanTarget(IPv4Math.NetworkAddress(address, prefixLength), prefixLength);
        return true;
    }

    private static bool TryParseRange(string token, out ScanTargetSpec target, out string? error)
    {
        target = new AutoLocalScanTarget();
        error = null;

        var separatorIndex = token.IndexOf('-');
        var firstPart = token[..separatorIndex].Trim();
        var lastPart = token[(separatorIndex + 1)..].Trim();

        if (!TryParseIPv4(firstPart, out var first))
        {
            error = $"Неверный диапазон: {token}";
            return false;
        }

        // Сокращение "212.59.101.1-254": вторая граница — только последний октет.
        if (!TryParseIPv4(lastPart, out var last))
        {
            if (!int.TryParse(lastPart, NumberStyles.None, CultureInfo.InvariantCulture, out var lastOctet) ||
                lastOctet is < 0 or > 255)
            {
                error = $"Неверный диапазон: {token}";
                return false;
            }

            last = IPv4Math.FromUInt32((IPv4Math.ToUInt32(first) & 0xFFFFFF00u) | (uint)lastOctet);
        }

        var firstValue = IPv4Math.ToUInt32(first);
        var lastValue = IPv4Math.ToUInt32(last);

        target = firstValue <= lastValue
            ? new RangeScanTarget(first, last)
            : new RangeScanTarget(last, first);

        return true;
    }

    private static bool TryParseIPv4(string value, out IPAddress address)
    {
        address = IPAddress.None;

        // IPAddress.TryParse принимает укороченные записи вроде "10.1", поэтому проверяем октеты явно.
        if (value.Count(static character => character == '.') != 3 ||
            !IPAddress.TryParse(value, out var parsed) ||
            !IPv4Math.IsIPv4(parsed))
        {
            return false;
        }

        address = parsed;
        return true;
    }

    private static bool TryParsePort(string value, out int port)
    {
        return int.TryParse(value.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out port) &&
               port is > 0 and <= 65535;
    }

    private static bool IsPlausibleHost(string value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > 253)
        {
            return false;
        }

        if (TryParseIPv4(value, out _))
        {
            return true;
        }

        // DNS-имя резолвится позже, здесь достаточно отсечь явный мусор.
        return value.All(static character =>
            char.IsAsciiLetterOrDigit(character) || character is '.' or '-' or '_');
    }
}
