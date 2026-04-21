using AdbControl.Application.Apk;
using AdbControl.Application.Devices;
using AdbControl.Application.Diagnostics;
using AdbControl.Application.Tools;
using AdbControl.Application.Workspace;
using AdbControl.Infrastructure.Devices;
using AdbControl.Infrastructure.Persistence;
using AdbControl.Shell.ViewModels;
using AdbControl.Shell.Views;
using AdbControl.Tools.CommandLog;
using AdbControl.Tools.Devices;
using AdbControl.Tools.Home;
using AdbControl.Tools.Library;
using AdbControl.Tools.Top;
using AdbControl.Tools.Apk;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Media.Imaging;

namespace AdbControl.App;

public partial class App : System.Windows.Application
{
    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        var storage = new LocalWorkspaceStorage(AppDataPaths.CreateDefault());
        storage.EnsureCreated();
        RegisterCrashLogging(storage.Paths);

        var deviceInventory = new DeviceInventoryState();
        IApkLibraryService apkLibrary = new LocalApkLibraryService(storage.Paths);
        var deviceAliasStore = new DeviceAliasFileStore(storage.Paths);
        var deviceAliases = new DeviceAliasCatalog(deviceAliasStore);
        await deviceAliases.InitializeAsync();
        var commandTraceStore = new CommandTraceFileStore(storage.Paths);
        var commandTraceJournal = new CommandTraceJournal(commandTraceStore);
        await commandTraceJournal.InitializeAsync();
        var adbProcessRunner = new AdbProcessRunner(commandTraceJournal);
        var deviceDiscovery = new LocalNetworkAdbDiscoveryService();
        var adbConnection = new AdbConnectionService(adbProcessRunner);
        IApkDeploymentService apkDeployment = new AdbApkDeploymentService(adbProcessRunner);
        var deviceActions = new AdbDeviceActionService(adbProcessRunner);
        var deviceTop = new AdbTopService(adbProcessRunner);

        IToolModule[] modules =
        [
            new HomeToolModule(),
            new DevicesToolModule(),
            new ApkToolModule(),
            new TopToolModule(),
            new CommandLogToolModule(),
            new ToolLibraryModule()
        ];

        ApplyThemeDictionaries(modules);

        var toolCatalog = new ToolCatalog(modules.SelectMany(x => x.GetToolRegistrations()));
        var workspace = new WorkspaceService(
            toolCatalog,
            deviceInventory,
            deviceAliases,
            apkLibrary,
            apkDeployment,
            deviceDiscovery,
            adbConnection,
            deviceActions,
            deviceTop,
            commandTraceJournal);
        workspace.EnsureStartupTabs();

        var shellWindow = new ShellWindow
        {
            DataContext = new ShellViewModel(toolCatalog, workspace, deviceInventory, commandTraceJournal),
            Icon = BitmapFrame.Create(new Uri("pack://application:,,,/AdbControl.App;component/Assets/icon_adb.ico", UriKind.Absolute))
        };

        MainWindow = shellWindow;
        shellWindow.Show();
    }

    private static void RegisterCrashLogging(AppDataPaths paths)
    {
        Current.DispatcherUnhandledException += (_, args) =>
        {
            AppendCrashLog(paths, args.Exception);
        };

        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
        {
            if (args.ExceptionObject is Exception exception)
            {
                AppendCrashLog(paths, exception);
            }
        };
    }

    private static void AppendCrashLog(AppDataPaths paths, Exception exception)
    {
        try
        {
            Directory.CreateDirectory(paths.LogsDirectory);
            var crashLogPath = Path.Combine(paths.LogsDirectory, "app-crash.log");
            var payload = new StringBuilder()
                .AppendLine($"[{DateTimeOffset.Now:O}]")
                .AppendLine(exception.ToString())
                .AppendLine()
                .ToString();

            File.AppendAllText(crashLogPath, payload, Encoding.UTF8);
        }
        catch
        {
            // Swallow logging failures to avoid recursive crash handling.
        }
    }

    private void ApplyThemeDictionaries(IEnumerable<IToolModule> modules)
    {
        Resources.MergedDictionaries.Add(
            (ResourceDictionary)LoadComponent(new Uri("/AdbControl.Shell;component/Styles/ShellTheme.xaml", UriKind.Relative)));

        foreach (var uri in modules.SelectMany(x => x.GetResourceDictionaryUris()))
        {
            Resources.MergedDictionaries.Add((ResourceDictionary)LoadComponent(uri));
        }
    }
}
