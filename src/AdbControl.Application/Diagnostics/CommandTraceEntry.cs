namespace AdbControl.Application.Diagnostics;

public sealed record CommandTraceEntry(
    Guid Id,
    DateTimeOffset Timestamp,
    string CommandText,
    string Stdout,
    string Stderr,
    int ExitCode,
    bool IsError);
