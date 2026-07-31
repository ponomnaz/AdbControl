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
