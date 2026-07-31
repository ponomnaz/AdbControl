using System.Collections.ObjectModel;
using AdbControl.Application.Apk;
using AdbControl.Application.Common;
using AdbControl.Application.Devices;
using AdbControl.Application.Diagnostics;
using AdbControl.Application.Logcat;
using AdbControl.Application.Remote;
using AdbControl.Application.Terminal;
using AdbControl.Application.Top;
using AdbControl.Application.Tools;

namespace AdbControl.Application.Workspace;

public sealed class WorkspaceService : ObservableObject
{
    private readonly ToolCatalog _toolCatalog;
    private readonly DeviceInventoryState _deviceInventory;
    private readonly DeviceAliasCatalog _deviceAliases;
    private readonly NetariumServerEndpointCatalog _netariumServerEndpoint;
    private readonly AutoConnectDeviceCatalog _autoConnectDevices;
    private readonly ScanProfileCatalog _scanProfiles;
    private readonly IApkLibraryService _apkLibrary;
    private readonly IApkDeploymentService _apkDeployment;
    private readonly IApkDevicePackageService _apkDevicePackages;
    private readonly IDeviceDiscoveryService _deviceDiscovery;
    private readonly IMdnsDiscoveryService _mdnsDiscovery;
    private readonly IAdbConnectionService _adbConnection;
    private readonly IDeviceActionService _deviceActions;
    private readonly IDeviceLogcatService _deviceLogcat;
    private readonly IAdbConsoleService _adbConsole;
    private readonly IDeviceTopService _deviceTop;
    private readonly IRemoteControlService _remoteControl;
    private readonly CommandTraceJournal _commandTraceJournal;
    private WorkspaceTab? _activeTab;

    public WorkspaceService(
        ToolCatalog toolCatalog,
        DeviceInventoryState deviceInventory,
        DeviceAliasCatalog deviceAliases,
        NetariumServerEndpointCatalog netariumServerEndpoint,
        AutoConnectDeviceCatalog autoConnectDevices,
        ScanProfileCatalog scanProfiles,
        IApkLibraryService apkLibrary,
        IApkDeploymentService apkDeployment,
        IApkDevicePackageService apkDevicePackages,
        IDeviceDiscoveryService deviceDiscovery,
        IMdnsDiscoveryService mdnsDiscovery,
        IAdbConnectionService adbConnection,
        IDeviceActionService deviceActions,
        IDeviceLogcatService deviceLogcat,
        IAdbConsoleService adbConsole,
        IDeviceTopService deviceTop,
        IRemoteControlService remoteControl,
        CommandTraceJournal commandTraceJournal)
    {
        _toolCatalog = toolCatalog;
        _deviceInventory = deviceInventory;
        _deviceAliases = deviceAliases;
        _netariumServerEndpoint = netariumServerEndpoint;
        _autoConnectDevices = autoConnectDevices;
        _scanProfiles = scanProfiles;
        _apkLibrary = apkLibrary;
        _apkDeployment = apkDeployment;
        _apkDevicePackages = apkDevicePackages;
        _deviceDiscovery = deviceDiscovery;
        _mdnsDiscovery = mdnsDiscovery;
        _adbConnection = adbConnection;
        _deviceActions = deviceActions;
        _deviceLogcat = deviceLogcat;
        _adbConsole = adbConsole;
        _deviceTop = deviceTop;
        _remoteControl = remoteControl;
        _commandTraceJournal = commandTraceJournal;
    }

    public ObservableCollection<WorkspaceTab> Tabs { get; } = [];

    public WorkspaceTab? ActiveTab
    {
        get => _activeTab;
        set
        {
            if (!SetProperty(ref _activeTab, value))
            {
                return;
            }

            foreach (var tab in Tabs)
            {
                tab.IsSelected = ReferenceEquals(tab, value);
            }
        }
    }

    public void EnsureStartupTabs()
    {
        if (Tabs.Count > 0)
        {
            return;
        }

        var startupTools = _toolCatalog.StartupTools;
        if (startupTools.Count == 0 && _toolCatalog.NavigationTools.Count > 0)
        {
            OpenTool(_toolCatalog.NavigationTools[0].Tool.Id);
            return;
        }

        foreach (var startupTool in startupTools)
        {
            OpenTool(startupTool.Tool.Id);
        }
    }

    public void OpenTool(string toolId)
    {
        var registration = _toolCatalog.GetRequired(toolId);

        if (registration.ReuseExistingTab)
        {
            var existingTab = Tabs.FirstOrDefault(x => string.Equals(x.Registration.Tool.Id, toolId, StringComparison.OrdinalIgnoreCase));
            if (existingTab is not null)
            {
                ActiveTab = existingTab;
                return;
            }
        }

        var contentViewModel = CreateToolContentViewModel(registration);
        var tab = new WorkspaceTab(registration, contentViewModel);
        Tabs.Add(tab);
        ActiveTab = tab;
    }

    public DetachedToolContent CreateDetachedToolContent(string toolId)
    {
        var registration = _toolCatalog.GetRequired(toolId);
        var contentViewModel = CreateToolContentViewModel(registration);
        return new DetachedToolContent(registration, contentViewModel);
    }

    public void CloseTab(WorkspaceTab? tab)
    {
        if (tab is null)
        {
            return;
        }

        var index = Tabs.IndexOf(tab);
        if (index < 0)
        {
            return;
        }

        Tabs.RemoveAt(index);

        if (tab.ContentViewModel is IDisposable disposable)
        {
            disposable.Dispose();
        }

        if (ReferenceEquals(ActiveTab, tab))
        {
            ActiveTab = Tabs.ElementAtOrDefault(Math.Min(index, Tabs.Count - 1));
        }
    }

    private object CreateToolContentViewModel(ToolRegistration registration)
    {
        var context = new ToolActivationContext(
            _toolCatalog,
            _deviceInventory,
            _deviceAliases,
            _netariumServerEndpoint,
            _autoConnectDevices,
            _scanProfiles,
            _apkLibrary,
            _apkDeployment,
            _apkDevicePackages,
            _deviceDiscovery,
            _mdnsDiscovery,
            _adbConnection,
            _deviceActions,
            _deviceLogcat,
            _adbConsole,
            _deviceTop,
            _remoteControl,
            _commandTraceJournal,
            OpenTool);

        return registration.CreateContentViewModel(context);
    }
}
