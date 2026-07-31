using System.Globalization;

namespace AdbControl.Application.Devices;

public readonly record struct PortRange(int First, int Last)
{
    public int Count => Last - First + 1;

    public override string ToString()
    {
        return First == Last
            ? First.ToString(CultureInfo.InvariantCulture)
            : $"{First}-{Last}";
    }
}

/// <summary>
/// Набор портов для проверки. По умолчанию — классический 5555.
/// </summary>
public sealed record PortSpec(IReadOnlyList<PortRange> Ranges)
{
    public const int DefaultAdbPort = 5555;

    /// <summary>Порт, поднимаемый через <c>adb tcpip</c>.</summary>
    public static PortSpec Default { get; } = new([new PortRange(DefaultAdbPort, DefaultAdbPort)]);

    /// <summary>Банк портов на случай нескольких <c>adb tcpip</c> на одном хосте.</summary>
    public static PortSpec TcpipBank { get; } = new([new PortRange(5555, 5585)]);

    public int Count => Ranges.Sum(static range => range.Count);

    public IEnumerable<int> Enumerate()
    {
        foreach (var range in Ranges)
        {
            for (var port = range.First; port <= range.Last; port++)
            {
                yield return port;
            }
        }
    }

    public override string ToString()
    {
        return string.Join(",", Ranges);
    }

    /// <summary>
    /// Разбирает запись вида "5555", "5555-5585", "5555,5560-5570".
    /// </summary>
    public static bool TryParse(string? value, out PortSpec spec, out string? error)
    {
        spec = Default;
        error = null;

        if (string.IsNullOrWhiteSpace(value))
        {
            return true;
        }

        var ranges = new List<PortRange>();

        foreach (var part in value.Split([',', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var separatorIndex = part.IndexOf('-');
            if (separatorIndex < 0)
            {
                if (!TryParsePort(part, out var single))
                {
                    error = $"Неверный порт: {part}";
                    return false;
                }

                ranges.Add(new PortRange(single, single));
                continue;
            }

            if (!TryParsePort(part[..separatorIndex], out var first) ||
                !TryParsePort(part[(separatorIndex + 1)..], out var last))
            {
                error = $"Неверный диапазон портов: {part}";
                return false;
            }

            ranges.Add(first <= last ? new PortRange(first, last) : new PortRange(last, first));
        }

        if (ranges.Count == 0)
        {
            error = "Не указан ни один порт.";
            return false;
        }

        spec = new PortSpec(ranges);
        return true;
    }

    private static bool TryParsePort(string value, out int port)
    {
        return int.TryParse(value.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out port) &&
               port is > 0 and <= 65535;
    }
}
