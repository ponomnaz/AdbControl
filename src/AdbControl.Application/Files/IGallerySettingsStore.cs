namespace AdbControl.Application.Files;

public interface IGallerySettingsStore
{
    Task<GallerySettings?> ReadAsync(CancellationToken cancellationToken = default);

    Task WriteAsync(GallerySettings settings, CancellationToken cancellationToken = default);
}
