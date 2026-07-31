namespace AdbControl.Application.Devices;

public interface IDeviceDiscoveryService
{
    /// <summary>
    /// Сканирует цели профиля, выдавая находки по мере обнаружения.
    /// Бросает <see cref="ScanBudgetExceededException"/>, если область больше лимита.
    /// </summary>
    IAsyncEnumerable<DiscoveredAdbEndpoint> DiscoverAsync(
        ScanProfile profile,
        IProgress<ScanProgress>? progress = null,
        CancellationToken cancellationToken = default);
}
