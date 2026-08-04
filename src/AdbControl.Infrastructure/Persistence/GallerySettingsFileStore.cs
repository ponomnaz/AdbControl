using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Unicode;
using AdbControl.Application.Files;

namespace AdbControl.Infrastructure.Persistence;

public sealed class GallerySettingsFileStore : IGallerySettingsStore
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.Create(UnicodeRanges.All)
    };

    private readonly string _filePath;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public GallerySettingsFileStore(AppDataPaths paths)
    {
        _filePath = Path.Combine(paths.SettingsDirectory, "gallery-settings.json");
    }

    public async Task<GallerySettings?> ReadAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (!File.Exists(_filePath))
            {
                return null;
            }

            await using var stream = new FileStream(_filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            return await JsonSerializer.DeserializeAsync<GallerySettings>(stream, SerializerOptions, cancellationToken);
        }
        catch (Exception)
        {
            // Испорченный файл настроек не должен мешать открыть вкладку.
            return null;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task WriteAsync(GallerySettings settings, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_filePath)!);

            await using var stream = new FileStream(_filePath, FileMode.Create, FileAccess.Write, FileShare.None);
            await JsonSerializer.SerializeAsync(stream, settings, SerializerOptions, cancellationToken);
        }
        catch (Exception)
        {
            // Настройки — не та вещь, ради которой стоит ронять вкладку.
        }
        finally
        {
            _gate.Release();
        }
    }
}
