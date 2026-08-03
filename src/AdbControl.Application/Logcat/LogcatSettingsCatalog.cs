using AdbControl.Application.Common;

namespace AdbControl.Application.Logcat;

public sealed class LogcatSettingsCatalog : ObservableObject
{
    private readonly ILogcatSettingsStore _store;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private LogcatSettings _settings = LogcatSettings.Default;

    public LogcatSettingsCatalog(ILogcatSettingsStore store)
    {
        _store = store;
    }

    public event EventHandler? Changed;

    public LogcatSettings Settings
    {
        get
        {
            _gate.Wait();
            try
            {
                return _settings;
            }
            finally
            {
                _gate.Release();
            }
        }
    }

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        var stored = Normalize(await _store.ReadAsync(cancellationToken) ?? LogcatSettings.Default);

        await _gate.WaitAsync(cancellationToken);
        try
        {
            _settings = stored;
        }
        finally
        {
            _gate.Release();
        }

        Changed?.Invoke(this, EventArgs.Empty);
    }

    public async Task SetSettingsAsync(LogcatSettings settings, CancellationToken cancellationToken = default)
    {
        var normalized = Normalize(settings);
        var changed = false;

        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (!IsSame(_settings, normalized))
            {
                _settings = normalized;
                changed = true;
            }
        }
        finally
        {
            _gate.Release();
        }

        if (!changed)
        {
            return;
        }

        await _store.WriteAsync(normalized, cancellationToken);
        Changed?.Invoke(this, EventArgs.Empty);
    }

    private static LogcatSettings Normalize(LogcatSettings settings)
    {
        return new LogcatSettings(
            new LogcatSearchTerms(
                NormalizeList(settings.Search.Include),
                NormalizeList(settings.Search.Exclude)),
            settings.Filter with
            {
                Tags = NormalizeList(settings.Filter.Tags),
                ExtraArguments = settings.Filter.ExtraArguments?.Trim() ?? string.Empty
            });
    }

    /// <summary>
    /// Пробелы не обрезаются: условие поиска вида " D " отличается от "D",
    /// и обрезка молча превращала бы одно в другое.
    /// </summary>
    private static string[] NormalizeList(IEnumerable<string>? values)
    {
        return (values ?? [])
            .Where(static value => !string.IsNullOrEmpty(value))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static bool IsSame(LogcatSettings left, LogcatSettings right)
    {
        return left.Search.Include.SequenceEqual(right.Search.Include, StringComparer.Ordinal) &&
               left.Search.Exclude.SequenceEqual(right.Search.Exclude, StringComparer.Ordinal) &&
               left.Filter.Level == right.Filter.Level &&
               left.Filter.OnlyNetarium == right.Filter.OnlyNetarium &&
               string.Equals(left.Filter.ExtraArguments, right.Filter.ExtraArguments, StringComparison.Ordinal) &&
               left.Filter.Tags.SequenceEqual(right.Filter.Tags, StringComparer.Ordinal);
    }
}
