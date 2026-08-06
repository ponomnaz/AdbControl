namespace AdbControl.Application.Remote;

/// <summary>
/// Что телевизор сообщает о своём экране. Физические значения — те, что зашиты в
/// устройство; переопределённые появляются только после <c>wm size</c> и <c>wm density</c>.
/// </summary>
public sealed record DeviceDisplayState(
    string? PhysicalSize,
    string? OverrideSize,
    int? PhysicalDensity,
    int? OverrideDensity)
{
    /// <summary>Что действует прямо сейчас.</summary>
    public string? EffectiveSize => OverrideSize ?? PhysicalSize;

    public int? EffectiveDensity => OverrideDensity ?? PhysicalDensity;

    public bool HasOverride => OverrideSize is not null || OverrideDensity is not null;
}
