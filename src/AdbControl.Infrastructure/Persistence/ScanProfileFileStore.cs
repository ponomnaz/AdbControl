using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Unicode;
using AdbControl.Application.Devices;

namespace AdbControl.Infrastructure.Persistence;

public sealed class ScanProfileFileStore : IScanProfileStore
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,

        // Без этого имена профилей уезжают в Офис,
        // а файл задуман пригодным для правки руками.
        Encoder = JavaScriptEncoder.Create(UnicodeRanges.All)
    };

    private readonly string _filePath;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public ScanProfileFileStore(AppDataPaths paths)
    {
        _filePath = Path.Combine(paths.SettingsDirectory, "scan-profiles.json");
    }

    public async Task<IReadOnlyList<SavedScanProfile>> ReadAllAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (!File.Exists(_filePath))
            {
                return [];
            }

            await using var stream = new FileStream(_filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);

            try
            {
                var profiles = await JsonSerializer.DeserializeAsync<SavedScanProfile[]>(
                    stream,
                    SerializerOptions,
                    cancellationToken);

                return profiles?
                    .Where(static profile => profile is not null && !string.IsNullOrWhiteSpace(profile.Name))
                    .Select(static profile => new SavedScanProfile(
                        profile.Name.Trim(),
                        profile.Targets?.Trim() ?? string.Empty,
                        profile.Ports?.Trim() ?? string.Empty))
                    .ToArray()
                    ?? [];
            }
            catch (JsonException)
            {
                return [];
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task WriteAllAsync(
        IReadOnlyList<SavedScanProfile> profiles,
        CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_filePath)!);

            var normalized = profiles
                .Where(static profile => !string.IsNullOrWhiteSpace(profile.Name))
                .OrderBy(static profile => profile.Name, StringComparer.CurrentCultureIgnoreCase)
                .ToArray();

            await using var stream = new FileStream(_filePath, FileMode.Create, FileAccess.Write, FileShare.Read);
            await JsonSerializer.SerializeAsync(stream, normalized, SerializerOptions, cancellationToken);
            await stream.FlushAsync(cancellationToken);
        }
        finally
        {
            _gate.Release();
        }
    }
}
