using AdbControl.Core.Devices;

namespace AdbControl.Application.Devices;

public interface IDeviceScreenshotService
{
    /// <summary>
    /// Снимает экран каждого устройства и сохраняет PNG в локальное хранилище.
    /// Возвращает пути сохранённых файлов в порядке съёмки.
    /// </summary>
    Task<ScreenshotBatchResult> CaptureAsync(
        IReadOnlyList<TvDeviceProfile> devices,
        CancellationToken cancellationToken = default);
}
