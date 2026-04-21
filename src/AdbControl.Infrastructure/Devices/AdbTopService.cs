using System.Text.RegularExpressions;
using AdbControl.Application.Top;
using AdbControl.Core.Devices;

namespace AdbControl.Infrastructure.Devices;

public sealed class AdbTopService : IDeviceTopService
{
    private static readonly Regex WhitespaceRegex = new(@"\s+", RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex AnsiEscapeRegex = new(@"\x1B(?:[@-Z\\-_]|\[[0-?]*[ -/]*[@-~])", RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex ControlCharacterRegex = new(@"[\u0000-\u0008\u000B\u000C\u000E-\u001F\u007F]", RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex SummaryLabelRegex = new(@"^\s*(?<label>[^:]+:)(?<tail>.*)$", RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly string[] TopCommandVariants =
    [
        "shell top -b -n 1",
        "shell top -m 30 -n 1",
        "shell top -n 1 -m 30",
        "shell top -n 1",
        "shell \"top -n 1\"",
        "shell toybox top -b -n 1"
    ];

    private readonly AdbProcessRunner _adbProcessRunner;

    public AdbTopService(AdbProcessRunner adbProcessRunner)
    {
        _adbProcessRunner = adbProcessRunner;
    }

    public async Task<DeviceTopSnapshotResult> CaptureAsync(TvDeviceProfile device, CancellationToken cancellationToken = default)
    {
        var targetId = GetTargetId(device);
        if (string.IsNullOrWhiteSpace(targetId))
        {
            return DeviceTopSnapshotResult.Failure("У устройства нет ADB-идентификатора.");
        }

        string? lastErrorMessage = null;

        foreach (var variant in TopCommandVariants)
        {
            var result = await _adbProcessRunner.RunAsync(
                $"-s {targetId} {variant}",
                cancellationToken,
                recordInJournal: false);

            if (!result.Started)
            {
                return DeviceTopSnapshotResult.Failure("adb.exe не найден.");
            }

            var stdout = NormalizeText(result.Stdout);
            var stderr = NormalizeText(result.Stderr);
            var cleanStdout = SanitizeTopOutput(stdout);
            var cleanStderr = SanitizeTopOutput(stderr);
            var combinedOutput = BuildCombinedOutput(cleanStdout, cleanStderr);

            if (LooksLikeTopOutput(cleanStdout) || LooksLikeTopOutput(combinedOutput))
            {
                return DeviceTopSnapshotResult.Success(ParseSnapshot(string.IsNullOrWhiteSpace(cleanStdout) ? combinedOutput : cleanStdout));
            }

            lastErrorMessage = ExtractErrorMessage(stdout, stderr, result.ExitCode);
        }

        return DeviceTopSnapshotResult.Failure(lastErrorMessage ?? "Не удалось получить top.");
    }

    private static DeviceTopSnapshot ParseSnapshot(string output)
    {
        var lines = output
            .Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(static line => line.TrimEnd())
            .Where(static line => !string.IsNullOrWhiteSpace(line))
            .ToArray();

        var headerIndex = FindHeaderIndex(lines);
        var summaryLines = headerIndex <= 0
            ? lines.Take(Math.Min(lines.Length, 4)).ToArray()
            : lines.Take(headerIndex).ToArray();
        var formattedSummaryLines = FormatSummaryLines(summaryLines);

        var tableLines = headerIndex < 0
            ? []
            : lines.Skip(headerIndex).ToArray();

        var processes = tableLines.Length == 0
            ? []
            : ParseProcesses(tableLines);

        return new DeviceTopSnapshot(
            DateTimeOffset.Now,
            string.Join(Environment.NewLine, formattedSummaryLines),
            BuildFormattedOutput(formattedSummaryLines, tableLines),
            processes);
    }

    private static int FindHeaderIndex(IReadOnlyList<string> lines)
    {
        for (var index = 0; index < lines.Count; index++)
        {
            var line = lines[index];
            if (!line.Contains("PID", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (line.Contains("COMMAND", StringComparison.OrdinalIgnoreCase) ||
                line.Contains("CMD", StringComparison.OrdinalIgnoreCase) ||
                line.Contains("ARGS", StringComparison.OrdinalIgnoreCase) ||
                line.Contains("NAME", StringComparison.OrdinalIgnoreCase))
            {
                return index;
            }
        }

        return -1;
    }

    private static IReadOnlyList<TopProcessEntry> ParseProcesses(IReadOnlyList<string> lines)
    {
        if (lines.Count == 0)
        {
            return [];
        }

        var headerTokens = SplitTokens(lines[0]);
        if (headerTokens.Length == 0)
        {
            return [];
        }

        var pidIndex = FindColumnIndex(headerTokens, "PID");
        var cpuIndex = FindColumnIndex(headerTokens, "%CPU", "CPU%", "CPU");
        var resIndex = FindColumnIndex(headerTokens, "RES", "RSS");
        var stateIndex = FindColumnIndex(headerTokens, "S", "STATE");
        var nameIndex = FindColumnIndex(headerTokens, "COMMAND", "CMD", "ARGS", "NAME");

        if (pidIndex < 0)
        {
            return [];
        }

        if (nameIndex < 0)
        {
            nameIndex = headerTokens.Length - 1;
        }

        var rows = new List<TopProcessEntry>();

        for (var index = 1; index < lines.Count; index++)
        {
            var line = lines[index].Trim();
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            if (line.StartsWith("Tasks:", StringComparison.OrdinalIgnoreCase) ||
                line.StartsWith("Mem:", StringComparison.OrdinalIgnoreCase) ||
                line.StartsWith("CPU:", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var tokens = SplitTokens(line);
            if (tokens.Length <= Math.Max(pidIndex, nameIndex))
            {
                continue;
            }

            var pid = SafeGet(tokens, pidIndex);
            if (string.IsNullOrWhiteSpace(pid) || !char.IsDigit(pid[0]))
            {
                continue;
            }

            rows.Add(new TopProcessEntry(
                pid,
                SafeGet(tokens, cpuIndex),
                SafeGet(tokens, resIndex),
                SafeGet(tokens, stateIndex),
                string.Join(" ", tokens.Skip(nameIndex))));
        }

        return rows;
    }

    private static int FindColumnIndex(IReadOnlyList<string> headers, params string[] names)
    {
        for (var index = 0; index < headers.Count; index++)
        {
            var header = headers[index];
            if (names.Any(name => string.Equals(header, name, StringComparison.OrdinalIgnoreCase)))
            {
                return index;
            }
        }

        return -1;
    }

    private static string[] SplitTokens(string line)
    {
        return WhitespaceRegex
            .Split(line.Trim())
            .Where(static token => !string.IsNullOrWhiteSpace(token))
            .ToArray();
    }

    private static string SafeGet(IReadOnlyList<string> tokens, int index)
    {
        return index >= 0 && index < tokens.Count
            ? tokens[index]
            : string.Empty;
    }

    private static string NormalizeText(string value)
    {
        return string.IsNullOrWhiteSpace(value)
            ? string.Empty
            : value.Replace("\r\n", "\n").Trim();
    }

    private static string SanitizeTopOutput(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var cleaned = AnsiEscapeRegex.Replace(value, string.Empty);
        cleaned = ControlCharacterRegex.Replace(cleaned, string.Empty);
        cleaned = TrimBeforeTopAnchor(cleaned);

        return cleaned.Trim();
    }

    private static bool LooksLikeTopOutput(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        return value.Contains("PID", StringComparison.OrdinalIgnoreCase) &&
               (value.Contains("CPU", StringComparison.OrdinalIgnoreCase) ||
                value.Contains("%CPU", StringComparison.OrdinalIgnoreCase) ||
                value.Contains("COMMAND", StringComparison.OrdinalIgnoreCase) ||
                value.Contains("NAME", StringComparison.OrdinalIgnoreCase));
    }

    private static string BuildCombinedOutput(string stdout, string stderr)
    {
        if (string.IsNullOrWhiteSpace(stdout))
        {
            return stderr;
        }

        if (string.IsNullOrWhiteSpace(stderr))
        {
            return stdout;
        }

        return $"{stdout}\n{stderr}";
    }

    private static string ExtractErrorMessage(string stdout, string stderr, int exitCode)
    {
        var candidate = !string.IsNullOrWhiteSpace(stderr)
            ? stderr
            : stdout;

        if (!string.IsNullOrWhiteSpace(candidate))
        {
            return candidate
                .Split('\n', StringSplitOptions.RemoveEmptyEntries)
                .Select(static line => line.Trim())
                .FirstOrDefault(static line => !string.IsNullOrWhiteSpace(line))
                ?? $"top завершился с кодом {exitCode}.";
        }

        return $"top завершился с кодом {exitCode}.";
    }

    private static string BuildFormattedOutput(IReadOnlyList<string> summaryLines, IReadOnlyList<string> tableLines)
    {
        if (tableLines.Count == 0)
        {
            return string.Join(Environment.NewLine, summaryLines);
        }

        var parts = new List<string>(summaryLines.Count + tableLines.Count + 1);
        parts.AddRange(summaryLines);

        if (summaryLines.Count > 0)
        {
            parts.Add(string.Empty);
        }

        parts.AddRange(FormatTableLines(tableLines));
        return string.Join(Environment.NewLine, parts);
    }

    private static IReadOnlyList<string> FormatSummaryLines(IReadOnlyList<string> summaryLines)
    {
        if (summaryLines.Count == 0)
        {
            return [];
        }

        var matches = summaryLines
            .Select(line => SummaryLabelRegex.Match(line))
            .ToArray();

        var labelWidth = matches
            .Where(static match => match.Success)
            .Select(match => match.Groups["label"].Value.Trim().Length)
            .DefaultIfEmpty(0)
            .Max();

        if (labelWidth == 0)
        {
            return summaryLines.Select(static line => line.TrimStart()).ToArray();
        }

        var formatted = new string[summaryLines.Count];
        for (var index = 0; index < summaryLines.Count; index++)
        {
            var line = summaryLines[index];
            var match = matches[index];
            if (!match.Success)
            {
                formatted[index] = line.TrimStart();
                continue;
            }

            var label = match.Groups["label"].Value.Trim();
            var tail = match.Groups["tail"].Value;
            formatted[index] = label.PadRight(labelWidth) + tail;
        }

        return formatted;
    }

    private static IReadOnlyList<string> FormatTableLines(IReadOnlyList<string> tableLines)
    {
        if (tableLines.Count == 0)
        {
            return [];
        }

        var headerTokens = NormalizeTableTokens(tableLines[0]);
        if (headerTokens.Length == 0)
        {
            return tableLines;
        }

        var rows = new List<string[]>(tableLines.Count) { headerTokens };

        foreach (var line in tableLines.Skip(1))
        {
            var tokens = NormalizeTableTokens(line);
            if (tokens.Length == 0)
            {
                continue;
            }

            rows.Add(StretchTokens(tokens, headerTokens.Length));
        }

        if (rows.Count == 1)
        {
            return [string.Join("  ", headerTokens)];
        }

        var widths = new int[headerTokens.Length];
        for (var columnIndex = 0; columnIndex < headerTokens.Length - 1; columnIndex++)
        {
            widths[columnIndex] = rows.Max(row => SafeGet(row, columnIndex).Length);
        }

        return rows
            .Select(row => FormatRow(row, widths))
            .ToArray();
    }

    private static string[] NormalizeTableTokens(string line)
    {
        return SplitTokens(line)
            .Select(static token => token.Replace("S[%CPU]", "S").Replace("[%CPU]", "%CPU"))
            .ToArray();
    }

    private static string[] StretchTokens(string[] tokens, int targetColumnCount)
    {
        if (tokens.Length == targetColumnCount)
        {
            return tokens;
        }

        if (tokens.Length < targetColumnCount)
        {
            return [.. tokens, .. Enumerable.Repeat(string.Empty, targetColumnCount - tokens.Length)];
        }

        var result = new string[targetColumnCount];
        for (var index = 0; index < targetColumnCount - 1; index++)
        {
            result[index] = index < tokens.Length ? tokens[index] : string.Empty;
        }

        result[targetColumnCount - 1] = string.Join(" ", tokens.Skip(targetColumnCount - 1));
        return result;
    }

    private static string FormatRow(IReadOnlyList<string> columns, IReadOnlyList<int> widths)
    {
        var builder = new System.Text.StringBuilder();

        for (var index = 0; index < columns.Count; index++)
        {
            var value = columns[index];
            if (index == columns.Count - 1)
            {
                builder.Append(value);
                continue;
            }

            builder.Append(value.PadRight(widths[index]));
            builder.Append("  ");
        }

        return builder.ToString().TrimEnd();
    }

    private static string TrimBeforeTopAnchor(string value)
    {
        var anchors = new[]
        {
            "Tasks:",
            "Mem:",
            "Swap:",
            "CPU:",
            "PID "
        };

        var firstAnchorIndex = anchors
            .Select(anchor => value.IndexOf(anchor, StringComparison.OrdinalIgnoreCase))
            .Where(index => index >= 0)
            .DefaultIfEmpty(-1)
            .Min();

        return firstAnchorIndex > 0
            ? value[firstAnchorIndex..]
            : value;
    }

    private static string GetTargetId(TvDeviceProfile device)
    {
        return string.IsNullOrWhiteSpace(device.NetworkEndpoint)
            ? device.Id
            : device.NetworkEndpoint;
    }
}
