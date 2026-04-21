using System.Text.Json;
using AdbControl.Application.Devices;

namespace AdbControl.Infrastructure.Persistence;

public sealed class DeviceAliasFileStore : IDeviceAliasStore
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    private readonly string _filePath;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public DeviceAliasFileStore(AppDataPaths paths)
    {
        _filePath = Path.Combine(paths.SettingsDirectory, "device-aliases.json");
    }

    public async Task<IReadOnlyDictionary<string, string>> ReadAllAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (!File.Exists(_filePath))
            {
                return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            }

            await using var stream = new FileStream(_filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);

            try
            {
                var aliases = await JsonSerializer.DeserializeAsync<Dictionary<string, string>>(stream, SerializerOptions, cancellationToken);
                if (aliases is null)
                {
                    return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                }

                return aliases
                    .Where(static pair => !string.IsNullOrWhiteSpace(pair.Key) && !string.IsNullOrWhiteSpace(pair.Value))
                    .ToDictionary(
                        static pair => pair.Key.Trim(),
                        static pair => pair.Value.Trim(),
                        StringComparer.OrdinalIgnoreCase);
            }
            catch (JsonException)
            {
                return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task WriteAllAsync(IReadOnlyDictionary<string, string> aliases, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_filePath)!);

            var normalized = aliases
                .Where(static pair => !string.IsNullOrWhiteSpace(pair.Key) && !string.IsNullOrWhiteSpace(pair.Value))
                .ToDictionary(
                    static pair => pair.Key.Trim(),
                    static pair => pair.Value.Trim(),
                    StringComparer.OrdinalIgnoreCase);

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
