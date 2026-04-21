using AdbControl.Application.Common;

namespace AdbControl.Application.Devices;

public sealed class DeviceAliasCatalog : ObservableObject
{
    private readonly IDeviceAliasStore _store;
    private readonly Dictionary<string, string> _aliases = new(StringComparer.OrdinalIgnoreCase);
    private readonly SemaphoreSlim _gate = new(1, 1);

    public DeviceAliasCatalog(IDeviceAliasStore store)
    {
        _store = store;
    }

    public event EventHandler? Changed;

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        var aliases = await _store.ReadAllAsync(cancellationToken);

        await _gate.WaitAsync(cancellationToken);
        try
        {
            _aliases.Clear();

            foreach (var pair in aliases)
            {
                var endpoint = NormalizeEndpoint(pair.Key);
                var alias = NormalizeAlias(pair.Value);
                if (endpoint is null || alias is null)
                {
                    continue;
                }

                _aliases[endpoint] = alias;
            }
        }
        finally
        {
            _gate.Release();
        }

        Changed?.Invoke(this, EventArgs.Empty);
    }

    public string? GetAlias(string? endpoint)
    {
        var normalizedEndpoint = NormalizeEndpoint(endpoint);
        if (normalizedEndpoint is null)
        {
            return null;
        }

        return _aliases.TryGetValue(normalizedEndpoint, out var alias)
            ? alias
            : null;
    }

    public async Task SetAliasAsync(string endpoint, string? alias, CancellationToken cancellationToken = default)
    {
        var normalizedEndpoint = NormalizeEndpoint(endpoint)
            ?? throw new ArgumentException("Endpoint is required.", nameof(endpoint));

        var normalizedAlias = NormalizeAlias(alias);
        IReadOnlyDictionary<string, string>? snapshot = null;
        var changed = false;

        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (normalizedAlias is null)
            {
                changed = _aliases.Remove(normalizedEndpoint);
            }
            else if (!_aliases.TryGetValue(normalizedEndpoint, out var existingAlias) ||
                     !string.Equals(existingAlias, normalizedAlias, StringComparison.Ordinal))
            {
                _aliases[normalizedEndpoint] = normalizedAlias;
                changed = true;
            }

            if (changed)
            {
                snapshot = _aliases.ToDictionary(static pair => pair.Key, static pair => pair.Value, StringComparer.OrdinalIgnoreCase);
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

    private static string? NormalizeAlias(string? alias)
    {
        return string.IsNullOrWhiteSpace(alias)
            ? null
            : alias.Trim();
    }
}
