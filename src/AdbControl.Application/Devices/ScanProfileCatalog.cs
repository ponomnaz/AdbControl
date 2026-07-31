using AdbControl.Application.Common;

namespace AdbControl.Application.Devices;

public sealed class ScanProfileCatalog : ObservableObject
{
    private readonly IScanProfileStore _store;
    private readonly List<SavedScanProfile> _profiles = [];
    private readonly SemaphoreSlim _gate = new(1, 1);

    public ScanProfileCatalog(IScanProfileStore store)
    {
        _store = store;
    }

    public event EventHandler? Changed;

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        var profiles = await _store.ReadAllAsync(cancellationToken);

        await _gate.WaitAsync(cancellationToken);
        try
        {
            _profiles.Clear();
            _profiles.AddRange(profiles.Where(static profile => !string.IsNullOrWhiteSpace(profile.Name)));
        }
        finally
        {
            _gate.Release();
        }

        Changed?.Invoke(this, EventArgs.Empty);
    }

    public IReadOnlyList<SavedScanProfile> GetProfiles()
    {
        _gate.Wait();
        try
        {
            return _profiles
                .OrderBy(static profile => profile.Name, StringComparer.CurrentCultureIgnoreCase)
                .ToArray();
        }
        finally
        {
            _gate.Release();
        }
    }

    public SavedScanProfile? Find(string? name)
    {
        var normalizedName = Normalize(name);
        if (normalizedName is null)
        {
            return null;
        }

        _gate.Wait();
        try
        {
            return _profiles.FirstOrDefault(profile =>
                string.Equals(profile.Name, normalizedName, StringComparison.CurrentCultureIgnoreCase));
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>Сохраняет профиль, перезаписывая одноимённый.</summary>
    public async Task SaveAsync(SavedScanProfile profile, CancellationToken cancellationToken = default)
    {
        var normalizedName = Normalize(profile.Name)
            ?? throw new ArgumentException("Имя профиля обязательно.", nameof(profile));

        var normalizedProfile = profile with
        {
            Name = normalizedName,
            Targets = profile.Targets.Trim(),
            Ports = profile.Ports.Trim()
        };

        IReadOnlyList<SavedScanProfile> snapshot;

        await _gate.WaitAsync(cancellationToken);
        try
        {
            _profiles.RemoveAll(existing =>
                string.Equals(existing.Name, normalizedName, StringComparison.CurrentCultureIgnoreCase));

            _profiles.Add(normalizedProfile);
            snapshot = _profiles.ToArray();
        }
        finally
        {
            _gate.Release();
        }

        await _store.WriteAllAsync(snapshot, cancellationToken);
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public async Task DeleteAsync(string name, CancellationToken cancellationToken = default)
    {
        var normalizedName = Normalize(name);
        if (normalizedName is null)
        {
            return;
        }

        IReadOnlyList<SavedScanProfile> snapshot;
        int removed;

        await _gate.WaitAsync(cancellationToken);
        try
        {
            removed = _profiles.RemoveAll(existing =>
                string.Equals(existing.Name, normalizedName, StringComparison.CurrentCultureIgnoreCase));

            snapshot = _profiles.ToArray();
        }
        finally
        {
            _gate.Release();
        }

        if (removed == 0)
        {
            return;
        }

        await _store.WriteAllAsync(snapshot, cancellationToken);
        Changed?.Invoke(this, EventArgs.Empty);
    }

    private static string? Normalize(string? name)
    {
        return string.IsNullOrWhiteSpace(name) ? null : name.Trim();
    }
}
