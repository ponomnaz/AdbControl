using System.Text;
using System.Text.Json;
using AdbControl.Application.Diagnostics;

namespace AdbControl.Infrastructure.Persistence;

public sealed class CommandTraceFileStore : ICommandTraceStore
{
    private const int MaxFileSizeBytes = 1_048_576;
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);
    private readonly string _filePath;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public CommandTraceFileStore(AppDataPaths paths)
    {
        _filePath = Path.Combine(paths.LogsDirectory, "command-trace.jsonl");
    }

    public async Task<IReadOnlyList<CommandTraceEntry>> ReadAllAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (!File.Exists(_filePath))
            {
                return [];
            }

            var entries = new List<CommandTraceEntry>();
            await using var stream = new FileStream(_filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            using var reader = new StreamReader(stream, Encoding.UTF8);

            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var line = await reader.ReadLineAsync(cancellationToken);
                if (line is null)
                {
                    break;
                }

                if (string.IsNullOrWhiteSpace(line))
                {
                    continue;
                }

                try
                {
                    var entry = JsonSerializer.Deserialize<CommandTraceEntry>(line, SerializerOptions);
                    if (entry is not null)
                    {
                        entries.Add(entry);
                    }
                }
                catch (JsonException)
                {
                    // Skip malformed lines so one bad record does not break the whole journal.
                }
            }

            return entries;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task AppendAsync(CommandTraceEntry entry, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_filePath)!);

            var line = JsonSerializer.Serialize(entry, SerializerOptions);
            await File.AppendAllTextAsync(_filePath, line + Environment.NewLine, Encoding.UTF8, cancellationToken);

            var fileInfo = new FileInfo(_filePath);
            if (fileInfo.Exists && fileInfo.Length > MaxFileSizeBytes)
            {
                await TrimFileAsync(cancellationToken);
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task RewriteAsync(IReadOnlyList<CommandTraceEntry> entries, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_filePath)!);

            if (entries.Count == 0)
            {
                await File.WriteAllTextAsync(_filePath, string.Empty, Encoding.UTF8, cancellationToken);
                return;
            }

            var lines = entries
                .Select(entry => JsonSerializer.Serialize(entry, SerializerOptions))
                .ToArray();

            await File.WriteAllLinesAsync(_filePath, lines, Encoding.UTF8, cancellationToken);
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task TrimFileAsync(CancellationToken cancellationToken)
    {
        var lines = await File.ReadAllLinesAsync(_filePath, cancellationToken);
        if (lines.Length == 0)
        {
            return;
        }

        var retainedLines = new List<string>();
        var currentSize = 0;

        for (var index = lines.Length - 1; index >= 0; index--)
        {
            var line = lines[index];
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            var lineSize = Encoding.UTF8.GetByteCount(line + Environment.NewLine);
            if (retainedLines.Count > 0 && currentSize + lineSize > MaxFileSizeBytes)
            {
                break;
            }

            retainedLines.Add(line);
            currentSize += lineSize;
        }

        retainedLines.Reverse();
        await File.WriteAllLinesAsync(_filePath, retainedLines, Encoding.UTF8, cancellationToken);
    }
}
