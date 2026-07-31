namespace AdbControl.Application.Devices;

public sealed record ScanProfile(
    string Name,
    IReadOnlyList<ScanTargetSpec> Targets,
    PortSpec Ports,
    ScanTuning Tuning)
{
    /// <summary>Поведение по умолчанию: подсети машины, порт 5555.</summary>
    public static ScanProfile LocalNetwork { get; } = new(
        "Локальная сеть",
        [new AutoLocalScanTarget()],
        PortSpec.Default,
        ScanTuning.Default);

    /// <summary>
    /// Перебор всего диапазона беспроводной отладки на одном хосте. Только по явной команде:
    /// 35 тысяч проб для подсети недопустимы, для одного адреса — приемлемы.
    /// Нужен там, где mDNS не долетает, — например, за пробросом.
    /// </summary>
    public static ScanProfile DeepHostScan(ScanTargetSpec target)
    {
        return new ScanProfile(
            $"Глубокий скан {target.Label}",
            [target],
            new PortSpec([new PortRange(30000, 65535)]),
            ScanTuning.Default with
            {
                MaxProbeBudget = 40000,
                LocalParallelism = 512,
                RemoteParallelism = 128
            });
    }

    public static ScanProfile ForTargets(
        IReadOnlyList<ScanTargetSpec> targets,
        PortSpec? ports = null,
        ScanTuning? tuning = null)
    {
        return new ScanProfile(
            string.Join(", ", targets.Select(static target => target.Label)),
            targets,
            ports ?? PortSpec.Default,
            tuning ?? ScanTuning.Default);
    }
}
