using AdbControl.Application.Devices;
using AdbControl.Application.Tools;
using AdbControl.Application.Workspace;
using AdbControl.Infrastructure.Devices;
using AdbControl.Infrastructure.Persistence;
using AdbControl.Shell.ViewModels;
using AdbControl.Shell.Views;
using AdbControl.Tools.Devices;
using AdbControl.Tools.Home;
using AdbControl.Tools.Library;
using System.Windows;
using System.Windows.Media.Imaging;

namespace AdbControl.App;

public partial class App : System.Windows.Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        var storage = new LocalWorkspaceStorage(AppDataPaths.CreateDefault());
        storage.EnsureCreated();

        var deviceInventory = new DeviceInventoryState();
        var deviceDiscovery = new LocalNetworkAdbDiscoveryService();
        var adbConnection = new AdbConnectionService();
        var deviceActions = new AdbDeviceActionService();

        IToolModule[] modules =
        [
            new HomeToolModule(),
            new DevicesToolModule(),
            new ToolLibraryModule()
        ];

        ApplyThemeDictionaries(modules);

        var toolCatalog = new ToolCatalog(modules.SelectMany(x => x.GetToolRegistrations()));
        var workspace = new WorkspaceService(toolCatalog, deviceInventory, deviceDiscovery, adbConnection, deviceActions);
        workspace.EnsureStartupTabs();

        var shellWindow = new ShellWindow
        {
            DataContext = new ShellViewModel(toolCatalog, workspace, deviceInventory),
            Icon = BitmapFrame.Create(new Uri("pack://application:,,,/AdbControl.App;component/Assets/icon_adb.ico", UriKind.Absolute))
        };

        MainWindow = shellWindow;
        shellWindow.Show();
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
