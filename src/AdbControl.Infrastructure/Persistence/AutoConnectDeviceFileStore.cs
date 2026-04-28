using System.Text.Json;
using AdbControl.Application.Devices;

namespace AdbControl.Infrastructure.Persistence;

public sealed class AutoConnectDeviceFileStore : IAutoConnectDeviceStore
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    private readonly string _filePath;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public AutoConnectDeviceFileStore(AppDataPaths paths)
    {
        _filePath = Path.Combine(paths.SettingsDirectory, "device-auto-connect.json");
    }

    public async Task<IReadOnlyList<string>> ReadAllAsync(CancellationToken cancellationToken = default)
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
                var endpoints = await JsonSerializer.DeserializeAsync<string[]>(stream, SerializerOptions, cancellationToken);
                return endpoints?
                    .Where(static endpoint => !string.IsNullOrWhiteSpace(endpoint))
                    .Select(static endpoint => endpoint.Trim())
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .OrderBy(static endpoint => endpoint, StringComparer.OrdinalIgnoreCase)
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

    public async Task WriteAllAsync(IReadOnlyList<string> endpoints, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_filePath)!);

            var normalized = endpoints
                .Where(static endpoint => !string.IsNullOrWhiteSpace(endpoint))
                .Select(static endpoint => endpoint.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(static endpoint => endpoint, StringComparer.OrdinalIgnoreCase)
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
