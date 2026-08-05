namespace AdbControl.Application.Diagnostics;

public sealed record CommandTraceEntry(
    Guid Id,
    DateTimeOffset Timestamp,
    string CommandText,
    string Stdout,
    string Stderr,
    int ExitCode,
    bool IsError,
    /// <summary>
    /// Сколько заняла команда. Необязательное: записи, сделанные до появления поля,
    /// читаются из файла без него, и подделывать им длительность нечем.
    /// </summary>
    int? DurationMs = null);
