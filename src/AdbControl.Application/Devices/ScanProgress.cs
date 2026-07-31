namespace AdbControl.Application.Devices;

public sealed record ScanProgress(int CompletedProbes, int TotalProbes, int FoundCount)
{
    public double Fraction => TotalProbes <= 0
        ? 0d
        : Math.Clamp((double)CompletedProbes / TotalProbes, 0d, 1d);
}
