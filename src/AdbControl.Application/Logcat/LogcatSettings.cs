namespace AdbControl.Application.Logcat;

/// <summary>Сохраняемое состояние вкладки logcat: фильтр устройства и условия отбора на экране.</summary>
public sealed record LogcatSettings(LogcatSearchTerms Search, LogcatFilterSettings Filter)
{
    public static LogcatSettings Default { get; } = new(LogcatSearchTerms.Empty, LogcatFilterSettings.Default);
}
