namespace AdbControl.Infrastructure.Persistence;

public sealed class LocalWorkspaceStorage
{
    public LocalWorkspaceStorage(AppDataPaths paths)
    {
        Paths = paths;
    }

    public AppDataPaths Paths { get; }

    public void EnsureCreated()
    {
        Directory.CreateDirectory(Paths.RootDirectory);
        Directory.CreateDirectory(Paths.SettingsDirectory);
        Directory.CreateDirectory(Paths.PresetsDirectory);
        Directory.CreateDirectory(Paths.LayoutDirectory);
        Directory.CreateDirectory(Paths.LogsDirectory);
        Directory.CreateDirectory(Paths.CacheDirectory);
        Directory.CreateDirectory(Paths.ApksDirectory);
        Directory.CreateDirectory(Paths.SessionsDirectory);
        Directory.CreateDirectory(Paths.ExportsDirectory);
    }
}
