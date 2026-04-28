using AdbControl.Application.Common;

namespace AdbControl.Application.Devices;

public sealed class AutoConnectDeviceCatalog : ObservableObject
{
    private readonly IAutoConnectDeviceStore _store;
    private readonly HashSet<string> _endpoints = new(StringComparer.OrdinalIgnoreCase);
    private readonly SemaphoreSlim _gate = new(1, 1);

    public AutoConnectDeviceCatalog(IAutoConnectDeviceStore store)
    {
        _store = store;
    }

    public event EventHandler? Changed;

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        var endpoints = await _store.ReadAllAsync(cancellationToken);

        await _gate.WaitAsync(cancellationToken);
        try
        {
            _endpoints.Clear();
            foreach (var endpoint in endpoints)
            {
                var normalized = NormalizeEndpoint(endpoint);
                if (normalized is not null)
                {
                    _endpoints.Add(normalized);
                }
            }
        }
        finally
        {
            _gate.Release();
        }

        Changed?.Invoke(this, EventArgs.Empty);
    }

    public IReadOnlyList<string> GetEndpoints()
    {
        _gate.Wait();
        try
        {
            return _endpoints
                .OrderBy(static endpoint => endpoint, StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }
        finally
        {
            _gate.Release();
        }
    }

    public bool Contains(string? endpoint)
    {
        var normalized = NormalizeEndpoint(endpoint);
        if (normalized is null)
        {
            return false;
        }

        _gate.Wait();
        try
        {
            return _endpoints.Contains(normalized);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task SetEnabledAsync(string endpoint, bool enabled, CancellationToken cancellationToken = default)
    {
        var normalizedEndpoint = NormalizeEndpoint(endpoint)
            ?? throw new ArgumentException("Endpoint is required.", nameof(endpoint));

        IReadOnlyList<string>? snapshot = null;
        var changed = false;

        await _gate.WaitAsync(cancellationToken);
        try
        {
            changed = enabled
                ? _endpoints.Add(normalizedEndpoint)
                : _endpoints.Remove(normalizedEndpoint);

            if (changed)
            {
                snapshot = _endpoints
                    .OrderBy(static value => value, StringComparer.OrdinalIgnoreCase)
                    .ToArray();
            }
        }
        finally
        {
            _gate.Release();
        }

        if (!changed || snapshot is null)
        {
            return;
        }

        await _store.WriteAllAsync(snapshot, cancellationToken);
        Changed?.Invoke(this, EventArgs.Empty);
    }

    private static string? NormalizeEndpoint(string? endpoint)
    {
        return string.IsNullOrWhiteSpace(endpoint)
            ? null
            : endpoint.Trim();
    }
}
