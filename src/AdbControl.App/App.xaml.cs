using AdbControl.Application.Apk;
using AdbControl.Application.Devices;
using AdbControl.Application.Diagnostics;
using AdbControl.Application.Logcat;
using AdbControl.Application.Remote;
using AdbControl.Application.Terminal;
using AdbControl.Application.Tools;
using AdbControl.Application.Workspace;
using AdbControl.Core.Devices;
using AdbControl.Infrastructure.Devices;
using AdbControl.Infrastructure.Persistence;
using AdbControl.Shell.ViewModels;
using AdbControl.Shell.Views;
using AdbControl.Tools.CommandLog;
using AdbControl.Tools.Console;
using AdbControl.Tools.Devices;
using AdbControl.Tools.Gallery;
using AdbControl.Tools.Home;
using AdbControl.Tools.Library;
using AdbControl.Tools.Logcat;
using AdbControl.Tools.Remote;
using AdbControl.Tools.Top;
using AdbControl.Tools.Apk;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace AdbControl.App;

public partial class App : System.Windows.Application
{
    private static readonly TimeSpan DeviceInventoryPollInterval = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan AutoConnectRetryDelay = TimeSpan.FromSeconds(3);

    private DispatcherTimer? _deviceInventoryTimer;

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
        var netariumServerEndpointStore = new NetariumServerEndpointFileStore(storage.Paths);
        var netariumServerEndpoint = new NetariumServerEndpointCatalog(netariumServerEndpointStore);
        await netariumServerEndpoint.InitializeAsync();
        var autoConnectStore = new AutoConnectDeviceFileStore(storage.Paths);
        var autoConnectDevices = new AutoConnectDeviceCatalog(autoConnectStore);
        await autoConnectDevices.InitializeAsync();
        var scanProfileStore = new ScanProfileFileStore(storage.Paths);
        var scanProfiles = new ScanProfileCatalog(scanProfileStore);
        await scanProfiles.InitializeAsync();
        var commandTraceStore = new CommandTraceFileStore(storage.Paths);
        var commandTraceJournal = new CommandTraceJournal(commandTraceStore);
        await commandTraceJournal.InitializeAsync();
        var adbProcessRunner = new AdbProcessRunner(commandTraceJournal);
        var deviceDiscovery = new NetworkAdbDiscoveryService();
        IMdnsDiscoveryService mdnsDiscovery = new AdbMdnsDiscoveryService(adbProcessRunner);
        var adbConnection = new AdbConnectionService(adbProcessRunner);
        var deviceInventorySync = new DeviceInventorySyncService(deviceInventory, adbConnection, deviceAliases);
        IApkDeploymentService apkDeployment = new AdbApkDeploymentService(adbProcessRunner);
        IApkDevicePackageService apkDevicePackages = new AdbApkDevicePackageService(adbProcessRunner);
        var deviceActions = new AdbDeviceActionService(adbProcessRunner);
        IDeviceScreenshotService deviceScreenshots = new AdbScreenshotService(storage.Paths, commandTraceJournal);
        IDeviceLogcatService deviceLogcat = new AdbLogcatService(adbProcessRunner, commandTraceJournal);
        var logcatSettings = new LogcatSettingsCatalog(new LogcatSettingsFileStore(storage.Paths));
        await logcatSettings.InitializeAsync();
        IAdbConsoleService adbConsole = new AdbConsoleService(adbProcessRunner);
        var deviceTop = new AdbTopService(adbProcessRunner, commandTraceJournal);
        IRemoteControlService remoteControl = new AdbRemoteControlService(adbProcessRunner, commandTraceJournal);

        IToolModule[] modules =
        [
            new HomeToolModule(),
            new DevicesToolModule(),
            new ApkToolModule(),
            new ConsoleToolModule(),
            new LogcatToolModule(),
            new TopToolModule(),
            new RemoteToolModule(),
            new GalleryToolModule(),
            new CommandLogToolModule(),
            new ToolLibraryModule()
        ];

        ApplyThemeDictionaries(modules);

        var toolCatalog = new ToolCatalog(modules.SelectMany(x => x.GetToolRegistrations()));
        var workspace = new WorkspaceService(
            toolCatalog,
            deviceInventory,
            deviceAliases,
            netariumServerEndpoint,
            autoConnectDevices,
            scanProfiles,
            apkLibrary,
            apkDeployment,
            apkDevicePackages,
            deviceDiscovery,
            mdnsDiscovery,
            deviceInventorySync,
            adbConnection,
            deviceActions,
            deviceScreenshots,
            deviceLogcat,
            logcatSettings,
            adbConsole,
            deviceTop,
            remoteControl,
            commandTraceJournal);
        workspace.EnsureStartupTabs();

        var shellWindow = new ShellWindow
        {
            DataContext = new ShellViewModel(toolCatalog, workspace, deviceInventory, commandTraceJournal),
            Icon = BitmapFrame.Create(new Uri("pack://application:,,,/AdbControl;component/Assets/icon_adb.ico", UriKind.Absolute))
        };

        MainWindow = shellWindow;
        shellWindow.Show();

        StartDeviceInventoryMonitor(deviceInventorySync);
        _ = TryAutoConnectOnStartupAsync(autoConnectDevices, adbConnection, deviceInventorySync);
    }

    /// <summary>
    /// Фоновое слежение за составом устройств. Таймер диспетчера, а не пула потоков:
    /// синхронизация меняет наблюдаемые коллекции, а они привязаны к потоку UI.
    /// </summary>
    private void StartDeviceInventoryMonitor(DeviceInventorySyncService deviceInventorySync)
    {
        _deviceInventoryTimer = new DispatcherTimer
        {
            Interval = DeviceInventoryPollInterval
        };

        _deviceInventoryTimer.Tick += (_, _) => _ = SafeSyncAsync(deviceInventorySync);
        _deviceInventoryTimer.Start();

        _ = SafeSyncAsync(deviceInventorySync);
    }

    private static async Task SafeSyncAsync(DeviceInventorySyncService deviceInventorySync)
    {
        try
        {
            await deviceInventorySync.SyncOnceAsync();
        }
        catch
        {
            // Опрос устройств не должен ронять приложение: следующий тик попробует снова.
        }
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

    private static async Task TryAutoConnectOnStartupAsync(
        AutoConnectDeviceCatalog autoConnectDevices,
        IAdbConnectionService adbConnection,
        DeviceInventorySyncService deviceInventorySync)
    {
        try
        {
            var endpoints = autoConnectDevices.GetEndpoints();
            if (endpoints.Count == 0)
            {
                return;
            }

            // Параллельно и с одним повтором: последовательный обход упирался в таймаут
            // каждого недоступного адреса, а первая попытка часто приходится на момент,
            // когда сеть после запуска системы ещё не поднялась.
            await Task.WhenAll(endpoints.Select(endpoint =>
                ConnectWithRetryAsync(adbConnection, endpoint)));

            // Инвентарь наполняет синхронизация — здесь достаточно её подтолкнуть.
            await deviceInventorySync.SyncOnceAsync();
        }
        catch
        {
            // Startup auto-connect should never break shell launch.
        }
    }

    private static async Task ConnectWithRetryAsync(IAdbConnectionService adbConnection, string endpoint)
    {
        try
        {
            var result = await adbConnection.ConnectAsync(endpoint);
            if (result.IsSuccess)
            {
                return;
            }

            await Task.Delay(AutoConnectRetryDelay);
            await adbConnection.ConnectAsync(endpoint);
        }
        catch
        {
            // Недоступный адрес не должен мешать остальным.
        }
    }
}
