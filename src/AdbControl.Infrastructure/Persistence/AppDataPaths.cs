namespace AdbControl.Infrastructure.Persistence;

public sealed class AppDataPaths
{
    public const string ProductDirectoryName = "AdbControl";

    private AppDataPaths(string rootDirectory)
    {
        RootDirectory = rootDirectory;
        SettingsDirectory = Path.Combine(rootDirectory, "settings");
        PresetsDirectory = Path.Combine(rootDirectory, "presets");
        LayoutDirectory = Path.Combine(rootDirectory, "layout");
        LogsDirectory = Path.Combine(rootDirectory, "logs");
        CacheDirectory = Path.Combine(rootDirectory, "cache");
        SessionsDirectory = Path.Combine(rootDirectory, "sessions");
        ExportsDirectory = Path.Combine(rootDirectory, "exports");
    }

    public string RootDirectory { get; }

    public string SettingsDirectory { get; }

    public string PresetsDirectory { get; }

    public string LayoutDirectory { get; }

    public string LogsDirectory { get; }

    public string CacheDirectory { get; }

    public string SessionsDirectory { get; }

    public string ExportsDirectory { get; }

    public static AppDataPaths CreateDefault()
    {
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        return new AppDataPaths(Path.Combine(localAppData, ProductDirectoryName));
    }
}
