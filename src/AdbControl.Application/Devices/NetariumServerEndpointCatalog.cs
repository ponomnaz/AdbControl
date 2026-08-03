using AdbControl.Application.Common;

namespace AdbControl.Application.Devices;

public sealed class NetariumServerEndpointCatalog : ObservableObject
{
    /// <summary>Адрес, который заводится в список при первом запуске.</summary>
    public const string DefaultServerEndpoint = "192.168.140.16:3602";

    private readonly INetariumServerEndpointStore _store;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly List<string> _endpoints = [];
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

    public IReadOnlyList<string> GetEndpoints()
    {
        _gate.Wait();
        try
        {
            return _endpoints.ToArray();
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        var stored = await _store.ReadAsync(cancellationToken);
        var endpoint = NormalizeEndpoint(stored?.Selected) ?? string.Empty;

        var endpoints = (stored?.Endpoints ?? [])
            .Select(NormalizeEndpoint)
            .OfType<string>()
            .ToList();

        // Заводим адрес по умолчанию только когда списка не было вовсе: на первом запуске
        // и в настройках прежнего формата. Очищенный вручную список остаётся пустым.
        var needsSeed = stored?.Endpoints is null;
        if (needsSeed)
        {
            endpoints.Add(DefaultServerEndpoint);

            if (!string.IsNullOrWhiteSpace(endpoint))
            {
                endpoints.Add(endpoint);
            }
        }

        await _gate.WaitAsync(cancellationToken);
        try
        {
            _endpoint = endpoint;
            ReplaceEndpoints(endpoints);
        }
        finally
        {
            _gate.Release();
        }

        if (needsSeed)
        {
            await PersistAsync(cancellationToken);
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

        await PersistAsync(cancellationToken);
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public async Task AddEndpointAsync(string? endpoint, CancellationToken cancellationToken = default)
    {
        var normalizedEndpoint = NormalizeEndpoint(endpoint)
            ?? throw new ArgumentException("Адрес сервера обязателен.", nameof(endpoint));

        var changed = false;

        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (!_endpoints.Contains(normalizedEndpoint, StringComparer.OrdinalIgnoreCase))
            {
                _endpoints.Add(normalizedEndpoint);
                ReplaceEndpoints(_endpoints.ToArray());
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

        await PersistAsync(cancellationToken);
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public async Task RemoveEndpointAsync(string? endpoint, CancellationToken cancellationToken = default)
    {
        var normalizedEndpoint = NormalizeEndpoint(endpoint);
        if (normalizedEndpoint is null)
        {
            return;
        }

        var changed = false;

        await _gate.WaitAsync(cancellationToken);
        try
        {
            changed = _endpoints.RemoveAll(existing =>
                string.Equals(existing, normalizedEndpoint, StringComparison.OrdinalIgnoreCase)) > 0;
        }
        finally
        {
            _gate.Release();
        }

        if (!changed)
        {
            return;
        }

        await PersistAsync(cancellationToken);
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

    /// <summary>Вызывается под удержанным <see cref="_gate"/>.</summary>
    private void ReplaceEndpoints(IEnumerable<string> endpoints)
    {
        var ordered = endpoints
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(static value => value, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        _endpoints.Clear();
        _endpoints.AddRange(ordered);
    }

    private async Task PersistAsync(CancellationToken cancellationToken)
    {
        NetariumServerEndpoints snapshot;

        await _gate.WaitAsync(cancellationToken);
        try
        {
            snapshot = new NetariumServerEndpoints(_endpoint, _endpoints.ToArray());
        }
        finally
        {
            _gate.Release();
        }

        await _store.WriteAsync(snapshot, cancellationToken);
    }
}
