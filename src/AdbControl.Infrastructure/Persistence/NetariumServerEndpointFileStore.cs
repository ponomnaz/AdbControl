using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Unicode;
using AdbControl.Application.Devices;

namespace AdbControl.Infrastructure.Persistence;

public sealed class NetariumServerEndpointFileStore : INetariumServerEndpointStore
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.Create(UnicodeRanges.All)
    };

    private readonly string _filePath;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public NetariumServerEndpointFileStore(AppDataPaths paths)
    {
        _filePath = Path.Combine(paths.SettingsDirectory, "netarium-server-endpoint.json");
    }

    public async Task<NetariumServerEndpoints?> ReadAsync(CancellationToken cancellationToken = default)
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
                var payload = await JsonSerializer.DeserializeAsync<NetariumServerEndpointPayload>(
                    stream,
                    SerializerOptions,
                    cancellationToken);

                if (payload is null)
                {
                    return null;
                }

                var selected = payload.Endpoint?.Trim() ?? string.Empty;

                // Файлы прежнего формата списка не содержат — null передаётся дальше как есть,
                // чтобы каталог отличил «списка не было» от «список очистили».
                return new NetariumServerEndpoints(
                    selected,
                    payload.Endpoints is null ? null : Normalize(payload.Endpoints));
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

    public async Task WriteAsync(NetariumServerEndpoints endpoints, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_filePath)!);

            var payload = new NetariumServerEndpointPayload(
                endpoints.Selected.Trim(),
                Normalize(endpoints.Endpoints ?? []));

            await using var stream = new FileStream(_filePath, FileMode.Create, FileAccess.Write, FileShare.Read);
            await JsonSerializer.SerializeAsync(stream, payload, SerializerOptions, cancellationToken);
            await stream.FlushAsync(cancellationToken);
        }
        finally
        {
            _gate.Release();
        }
    }

    private static string[] Normalize(IEnumerable<string?> endpoints)
    {
        return endpoints
            .Where(static endpoint => !string.IsNullOrWhiteSpace(endpoint))
            .Select(static endpoint => endpoint!.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(static endpoint => endpoint, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private sealed record NetariumServerEndpointPayload(string? Endpoint, string[]? Endpoints);
}
