using System.Text.Json;
using AdbControl.Application.Devices;

namespace AdbControl.Infrastructure.Persistence;

public sealed class NetariumServerEndpointFileStore : INetariumServerEndpointStore
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    private readonly string _filePath;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public NetariumServerEndpointFileStore(AppDataPaths paths)
    {
        _filePath = Path.Combine(paths.SettingsDirectory, "netarium-server-endpoint.json");
    }

    public async Task<string?> ReadAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (!File.Exists(_filePath))
            {
                return null;
            }

            await using var stream = new FileStream(_filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);

            try
            {
                var payload = await JsonSerializer.DeserializeAsync<NetariumServerEndpointPayload>(stream, SerializerOptions, cancellationToken);
                return payload?.Endpoint;
            }
            catch (JsonException)
            {
                return null;
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task WriteAsync(string endpoint, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_filePath)!);

            await using var stream = new FileStream(_filePath, FileMode.Create, FileAccess.Write, FileShare.Read);
            await JsonSerializer.SerializeAsync(stream, new NetariumServerEndpointPayload(endpoint), SerializerOptions, cancellationToken);
            await stream.FlushAsync(cancellationToken);
        }
        finally
        {
            _gate.Release();
        }
    }

    private sealed record NetariumServerEndpointPayload(string Endpoint);
}
