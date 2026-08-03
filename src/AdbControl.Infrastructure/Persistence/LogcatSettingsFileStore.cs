using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Unicode;
using AdbControl.Application.Logcat;

namespace AdbControl.Infrastructure.Persistence;

public sealed class LogcatSettingsFileStore : ILogcatSettingsStore
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.Create(UnicodeRanges.All)
    };

    private readonly string _filePath;
    private readonly string _legacySearchFilePath;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public LogcatSettingsFileStore(AppDataPaths paths)
    {
        _filePath = Path.Combine(paths.SettingsDirectory, "logcat-settings.json");
        _legacySearchFilePath = Path.Combine(paths.SettingsDirectory, "logcat-search.json");
    }

    public async Task<LogcatSettings?> ReadAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            // Условия отбора раньше лежали в отдельном файле — подхватываем их,
            // чтобы уже введённые плашки не пропали.
            var path = File.Exists(_filePath) ? _filePath : _legacySearchFilePath;
            if (!File.Exists(path))
            {
                return null;
            }

            await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);

            try
            {
                var payload = await JsonSerializer.DeserializeAsync<LogcatSettingsPayload>(
                    stream,
                    SerializerOptions,
                    cancellationToken);

                if (payload is null)
                {
                    return null;
                }

                return new LogcatSettings(
                    new LogcatSearchTerms(payload.Include ?? [], payload.Exclude ?? []),
                    new LogcatFilterSettings(
                        ParseLevel(payload.Level),
                        payload.Tags ?? [],
                        payload.OnlyNetarium ?? false,
                        payload.ExtraArguments ?? string.Empty));
            }
            catch (JsonException)
            {
                return null;
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task WriteAsync(LogcatSettings settings, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_filePath)!);

            var payload = new LogcatSettingsPayload(
                settings.Search.Include.ToArray(),
                settings.Search.Exclude.ToArray(),
                settings.Filter.Level.ToString(),
                settings.Filter.Tags.ToArray(),
                settings.Filter.OnlyNetarium,
                settings.Filter.ExtraArguments);

            await using var stream = new FileStream(_filePath, FileMode.Create, FileAccess.Write, FileShare.Read);
            await JsonSerializer.SerializeAsync(stream, payload, SerializerOptions, cancellationToken);
            await stream.FlushAsync(cancellationToken);
        }
        finally
        {
            _gate.Release();
        }
    }

    private static LogcatLevel ParseLevel(string? value)
    {
        return Enum.TryParse<LogcatLevel>(value, ignoreCase: true, out var level)
            ? level
            : LogcatLevel.All;
    }

    private sealed record LogcatSettingsPayload(
        string[]? Include,
        string[]? Exclude,
        string? Level,
        string[]? Tags,
        bool? OnlyNetarium,
        string? ExtraArguments);
}
