namespace AdbControl.Application.Top;

public sealed record DeviceTopSnapshot(
    DateTimeOffset CapturedAt,
    string SummaryText,
    string RawOutput,
    IReadOnlyList<string> Columns,
    IReadOnlyList<TopProcessEntry> Processes)
{
    /// <summary>Индекс колонки по имени; -1, если прошивка её не отдаёт.</summary>
    public int IndexOf(params string[] names)
    {
        for (var index = 0; index < Columns.Count; index++)
        {
            if (names.Any(name => string.Equals(Columns[index], name, StringComparison.OrdinalIgnoreCase)))
            {
                return index;
            }
        }

        return -1;
    }
}

/// <summary>
/// Строка таблицы top. Значения хранятся списком, а не именованными полями:
/// набор колонок различается между прошивками, и жёсткая запись ломалась бы на каждой второй.
/// </summary>
public sealed record TopProcessEntry(IReadOnlyList<string> Values)
{
    public string Get(int index)
    {
        return index >= 0 && index < Values.Count ? Values[index] : string.Empty;
    }
}
