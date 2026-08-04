using AdbControl.Core.Devices;

namespace AdbControl.Application.Devices;

public interface IDeviceScreenshotService
{
    /// <summary>Папка, в которую складываются снимки. Её же показывает вкладка «Скриншоты».</summary>
    string ScreenshotsDirectory { get; }

    /// <summary>
    /// Снимает экран каждого устройства и сохраняет PNG в локальное хранилище.
    /// Возвращает пути сохранённых файлов в порядке съёмки.
    /// </summary>
    Task<ScreenshotBatchResult> CaptureAsync(
        IReadOnlyList<TvDeviceProfile> devices,
        CancellationToken cancellationToken = default);
}
