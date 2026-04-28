namespace AdbControl.Application.Terminal;

public interface IAdbConsoleService
{
    Task<AdbConsoleCommandResult> ExecuteAsync(string rawCommand, CancellationToken cancellationToken = default);
}

public sealed record AdbConsoleCommandResult(
    string CommandText,
    bool Started,
    int ExitCode,
    string Stdout,
    string Stderr)
{
    public bool IsSuccess => Started && ExitCode == 0;

    public bool IsCanceled => ExitCode == -2;

    public bool HasOutput => !string.IsNullOrWhiteSpace(Stdout) || !string.IsNullOrWhiteSpace(Stderr);
}
