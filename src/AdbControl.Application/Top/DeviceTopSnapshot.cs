namespace AdbControl.Application.Top;

public sealed record DeviceTopSnapshot(
    DateTimeOffset CapturedAt,
    string SummaryText,
    string RawOutput,
    IReadOnlyList<TopProcessEntry> Processes);

public sealed record TopProcessEntry(
    string Pid,
    string Cpu,
    string Res,
    string State,
    string Name);
