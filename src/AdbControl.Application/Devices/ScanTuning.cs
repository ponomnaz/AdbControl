namespace AdbControl.Application.Devices;

/// <summary>
/// Параметры сканирования. Локальные и удалённые цели обслуживаются по-разному:
/// до хоста за NAT дольше RTT, а высокая параллельность забивает NAT-таблицу.
/// </summary>
public sealed record ScanTuning
{
    public static ScanTuning Default { get; } = new();

    public TimeSpan LocalConnectTimeout { get; init; } = TimeSpan.FromMilliseconds(400);

    public TimeSpan RemoteConnectTimeout { get; init; } = TimeSpan.FromMilliseconds(2500);

    public int LocalParallelism { get; init; } = 128;

    public int RemoteParallelism { get; init; } = 24;

    /// <summary>
    /// Подтверждать ADB рукопожатием, а не считать устройством любой открытый порт.
    /// Выполняется только для отозвавшихся адресов, поэтому на длительность скана почти не влияет.
    /// </summary>
    public bool VerifyAdbHandshake { get; init; } = true;

    /// <summary>Ответ на CNXN: RTT плюс время на реакцию adbd, поэтому запас больше, чем на connect.</summary>
    public TimeSpan LocalHandshakeTimeout { get; init; } = TimeSpan.FromMilliseconds(900);

    public TimeSpan RemoteHandshakeTimeout { get; init; } = TimeSpan.FromMilliseconds(3000);

    /// <summary>Предохранитель: максимум проб (адрес × порт) за один запуск.</summary>
    public int MaxProbeBudget { get; init; } = 4096;

    /// <summary>
    /// Ограничение автосканирования локальной подсети: /16 целиком перебирать нельзя,
    /// поэтому берётся ближайший блок такого размера вокруг собственного адреса.
    /// </summary>
    public int MaxAutoLocalHosts { get; init; } = 1024;
}
