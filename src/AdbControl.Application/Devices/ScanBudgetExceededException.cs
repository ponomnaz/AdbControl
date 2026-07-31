namespace AdbControl.Application.Devices;

/// <summary>
/// Сканирование не запущено: запрошено больше проб, чем разрешает <see cref="ScanTuning.MaxProbeBudget"/>.
/// Защита от случайного /8.
/// </summary>
public sealed class ScanBudgetExceededException : Exception
{
    public ScanBudgetExceededException(long requestedProbes, int budget)
        : base($"Слишком большая область сканирования: {requestedProbes} проверок при лимите {budget}. Сузь диапазон или список портов.")
    {
        RequestedProbes = requestedProbes;
        Budget = budget;
    }

    public long RequestedProbes { get; }

    public int Budget { get; }
}
