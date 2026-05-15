using AdbControl.Application.Common;

namespace AdbControl.Application.Devices;

public sealed class NetariumServerEndpointCatalog : ObservableObject
{
    private readonly INetariumServerEndpointStore _store;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private string _endpoint = string.Empty;

    public NetariumServerEndpointCatalog(INetariumServerEndpointStore store)
    {
        _store = store;
    }

    public event EventHandler? Changed;

    public string Endpoint
    {
        get
        {
            _gate.Wait();
            try
            {
                return _endpoint;
            }
            finally
            {
                _gate.Release();
            }
        }
    }

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        var endpoint = NormalizeEndpoint(await _store.ReadAsync(cancellationToken)) ?? string.Empty;

        await _gate.WaitAsync(cancellationToken);
        try
        {
            _endpoint = endpoint;
        }
        finally
        {
            _gate.Release();
        }

        Changed?.Invoke(this, EventArgs.Empty);
    }

    public async Task SetEndpointAsync(string? endpoint, CancellationToken cancellationToken = default)
    {
        var normalizedEndpoint = NormalizeEndpoint(endpoint) ?? string.Empty;
        var changed = false;

        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (!string.Equals(_endpoint, normalizedEndpoint, StringComparison.Ordinal))
            {
                _endpoint = normalizedEndpoint;
                changed = true;
            }
        }
        finally
        {
            _gate.Release();
        }

        if (!changed)
        {
            return;
        }

        await _store.WriteAsync(normalizedEndpoint, cancellationToken);
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public static string? NormalizeEndpoint(string? endpoint)
    {
        if (string.IsNullOrWhiteSpace(endpoint))
        {
            return null;
        }

        var trimmed = endpoint.Trim();
        return trimmed.StartsWith("tcp://", StringComparison.OrdinalIgnoreCase)
            ? trimmed["tcp://".Length..].Trim()
            : trimmed;
    }
}
