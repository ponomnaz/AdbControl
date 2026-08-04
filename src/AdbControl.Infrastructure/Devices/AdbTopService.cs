using System.Text.RegularExpressions;
using AdbControl.Application.Diagnostics;
using AdbControl.Application.Top;
using AdbControl.Core.Devices;

namespace AdbControl.Infrastructure.Devices;

public sealed class AdbTopService : IDeviceTopService
{
    private static readonly Regex WhitespaceRegex = new(@"\s+", RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex AnsiEscapeRegex = new(@"\x1B(?:[@-Z\\-_]|\[[0-?]*[ -/]*[@-~])", RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex ControlCharacterRegex = new(@"[\u0000-\u0008\u000B\u000C\u000E-\u001F\u007F]", RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex SummaryLabelRegex = new(@"^\s*(?<label>[^:]+:)(?<tail>.*)$", RegexOptions.Compiled | RegexOptions.CultureInvariant);
    /// <summary>
    /// Шаблоны команд: <c>{0}</c> подставляет ограничение числа процессов либо пустую строку.
    /// Прошивки отличаются набором поддерживаемых ключей, поэтому вариантов несколько.
    /// </summary>
    private static readonly string[] TopCommandTemplates =
    [
        "shell top -b -n 1{0}",
        "shell top -n 1{0}",
        "shell \"top -n 1{0}\"",
        "shell toybox top -b -n 1{0}"
    ];

    private readonly AdbProcessRunner _adbProcessRunner;
    private readonly CommandTraceJournal _commandTraceJournal;

    /// <summary>
    /// Вариант команды, сработавший на устройстве. Без этого перебор начинался бы
    /// с начала при каждом снимке: на прошивке, где годится четвёртый вариант,
    /// опрос стоил бы вчетверо дороже.
    /// </summary>
    private readonly Dictionary<string, string> _workingVariants = new(StringComparer.OrdinalIgnoreCase);

    public AdbTopService(AdbProcessRunner adbProcessRunner, CommandTraceJournal commandTraceJournal)
    {
        _adbProcessRunner = adbProcessRunner;
        _commandTraceJournal = commandTraceJournal;
    }

    public async Task<DeviceTopSnapshotResult> CaptureAsync(
        TvDeviceProfile device,
        int? processLimit = null,
        CancellationToken cancellationToken = default)
    {
        var targetId = GetTargetId(device);
        if (string.IsNullOrWhiteSpace(targetId))
        {
            return DeviceTopSnapshotResult.Failure("У устройства нет ADB-идентификатора.");
        }

        var limitFragment = processLimit is > 0 ? $" -m {processLimit}" : string.Empty;
        string? lastErrorMessage = null;

        foreach (var template in GetTemplateOrder(targetId))
        {
            var variant = string.Format(template, limitFragment);

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
                // Запоминаем шаблон, а не готовую строку: смена предела не должна
                // сбрасывать подобранный вариант.
                var wasRemembered = _workingVariants.TryGetValue(targetId, out var remembered) &&
                                    string.Equals(remembered, template, StringComparison.Ordinal);

                _workingVariants[targetId] = template;

                // В журнал пишем только смену рабочего варианта: опрос идёт раз в пару
                // секунд, и запись каждого снимка утопила бы остальные команды.
                if (!wasRemembered)
                {
                    await RecordAsync($"-s {targetId} {variant}", "Вариант команды top подобран.", false, cancellationToken);
                }

                return DeviceTopSnapshotResult.Success(ParseSnapshot(string.IsNullOrWhiteSpace(cleanStdout) ? combinedOutput : cleanStdout));
            }

            lastErrorMessage = ExtractErrorMessage(stdout, stderr, result.ExitCode);
        }

        _workingVariants.Remove(targetId);
        await RecordAsync($"-s {targetId} top", lastErrorMessage ?? "Не удалось получить top.", true, cancellationToken);

        return DeviceTopSnapshotResult.Failure(lastErrorMessage ?? "Не удалось получить top.");
    }

    private IEnumerable<string> GetTemplateOrder(string targetId)
    {
        _workingVariants.TryGetValue(targetId, out var remembered);

        if (remembered is not null)
        {
            yield return remembered;
        }

        foreach (var template in TopCommandTemplates)
        {
            if (!string.Equals(template, remembered, StringComparison.Ordinal))
            {
                yield return template;
            }
        }
    }

    private async Task RecordAsync(string arguments, string message, bool isError, CancellationToken cancellationToken)
    {
        try
        {
            await _commandTraceJournal.RecordAsync(
                new CommandTraceEntry(
                    Guid.NewGuid(),
                    DateTimeOffset.Now,
                    $"adb {arguments}",
                    isError ? string.Empty : message,
                    isError ? message : string.Empty,
                    isError ? 1 : 0,
                    isError),
                cancellationToken);
        }
        catch
        {
            // Журнал не должен мешать снятию top.
        }
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

        var columns = tableLines.Length == 0
            ? []
            : ExpandHeaderTokens(SplitTokens(tableLines[0]));

        var processes = columns.Count == 0
            ? []
            : ParseProcesses(columns, tableLines.Skip(1));

        return new DeviceTopSnapshot(
            DateTimeOffset.Now,
            string.Join(Environment.NewLine, formattedSummaryLines),
            BuildFormattedOutput(formattedSummaryLines, columns, processes),
            columns,
            processes);
    }

    /// <summary>
    /// toybox печатает состояние и загрузку одним заголовком <c>S[%CPU]</c>, хотя данных
    /// под ним две колонки. Без раскрытия заголовок короче строк на единицу, и всё,
    /// что правее, съезжает: %CPU читается как %MEM, %MEM как TIME+, а время липнет к имени.
    /// </summary>
    private static IReadOnlyList<string> ExpandHeaderTokens(IReadOnlyList<string> tokens)
    {
        var columns = new List<string>(tokens.Count + 1);

        foreach (var token in tokens)
        {
            var openIndex = token.IndexOf('[');
            if (openIndex > 0 && token.EndsWith(']'))
            {
                columns.Add(token[..openIndex]);
                columns.Add(token[(openIndex + 1)..^1]);
                continue;
            }

            columns.Add(token);
        }

        return columns;
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

    private static IReadOnlyList<TopProcessEntry> ParseProcesses(
        IReadOnlyList<string> columns,
        IEnumerable<string> lines)
    {
        var pidIndex = FindColumnIndex(columns, "PID");
        if (pidIndex < 0)
        {
            return [];
        }

        var rows = new List<TopProcessEntry>();

        foreach (var rawLine in lines)
        {
            var line = rawLine.Trim();
            if (line.Length == 0 ||
                line.StartsWith("Tasks:", StringComparison.OrdinalIgnoreCase) ||
                line.StartsWith("Mem:", StringComparison.OrdinalIgnoreCase) ||
                line.StartsWith("Swap:", StringComparison.OrdinalIgnoreCase) ||
                line.StartsWith("CPU:", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var tokens = SplitTokens(line);
            var pid = SafeGet(tokens, pidIndex);
            if (string.IsNullOrWhiteSpace(pid) || !char.IsDigit(pid[0]))
            {
                continue;
            }

            rows.Add(new TopProcessEntry(AlignToColumns(tokens, columns.Count)));
        }

        return rows;
    }

    /// <summary>
    /// Последняя колонка — имя процесса с аргументами, в ней бывают пробелы,
    /// поэтому хвост лишних токенов склеивается обратно.
    /// </summary>
    private static string[] AlignToColumns(IReadOnlyList<string> tokens, int columnCount)
    {
        var values = new string[columnCount];

        for (var index = 0; index < columnCount - 1; index++)
        {
            values[index] = SafeGet(tokens, index);
        }

        values[columnCount - 1] = tokens.Count >= columnCount
            ? string.Join(" ", tokens.Skip(columnCount - 1))
            : SafeGet(tokens, columnCount - 1);

        return values;
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

    private static string BuildFormattedOutput(
        IReadOnlyList<string> summaryLines,
        IReadOnlyList<string> columns,
        IReadOnlyList<TopProcessEntry> processes)
    {
        if (columns.Count == 0)
        {
            return string.Join(Environment.NewLine, summaryLines);
        }

        var rows = new List<IReadOnlyList<string>>(processes.Count + 1) { columns };
        rows.AddRange(processes.Select(process => process.Values));

        var widths = new int[columns.Count];
        for (var columnIndex = 0; columnIndex < columns.Count - 1; columnIndex++)
        {
            widths[columnIndex] = rows.Max(row => SafeGet(row, columnIndex).Length);
        }

        var parts = new List<string>(summaryLines.Count + rows.Count + 1);
        parts.AddRange(summaryLines);

        if (summaryLines.Count > 0)
        {
            parts.Add(string.Empty);
        }

        parts.AddRange(rows.Select(row => FormatRow(row, widths)));
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
