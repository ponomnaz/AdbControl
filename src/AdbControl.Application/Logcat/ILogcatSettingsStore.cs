namespace AdbControl.Application.Logcat;

public interface ILogcatSettingsStore
{
    Task<LogcatSettings?> ReadAsync(CancellationToken cancellationToken = default);

    Task WriteAsync(LogcatSettings settings, CancellationToken cancellationToken = default);
}
