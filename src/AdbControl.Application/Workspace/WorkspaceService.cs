using System.Collections.ObjectModel;
using AdbControl.Application.Common;
using AdbControl.Application.Devices;
using AdbControl.Application.Tools;

namespace AdbControl.Application.Workspace;

public sealed class WorkspaceService : ObservableObject
{
    private readonly ToolCatalog _toolCatalog;
    private readonly DeviceInventoryState _deviceInventory;
    private readonly IDeviceDiscoveryService _deviceDiscovery;
    private readonly IAdbConnectionService _adbConnection;
    private readonly IDeviceActionService _deviceActions;
    private WorkspaceTab? _activeTab;

    public WorkspaceService(
        ToolCatalog toolCatalog,
        DeviceInventoryState deviceInventory,
        IDeviceDiscoveryService deviceDiscovery,
        IAdbConnectionService adbConnection,
        IDeviceActionService deviceActions)
    {
        _toolCatalog = toolCatalog;
        _deviceInventory = deviceInventory;
        _deviceDiscovery = deviceDiscovery;
        _adbConnection = adbConnection;
        _deviceActions = deviceActions;
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

        var context = new ToolActivationContext(_toolCatalog, _deviceInventory, _deviceDiscovery, _adbConnection, _deviceActions, OpenTool);
        var contentViewModel = registration.CreateContentViewModel(context);
        var tab = new WorkspaceTab(registration, contentViewModel);
        Tabs.Add(tab);
        ActiveTab = tab;
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

        if (ReferenceEquals(ActiveTab, tab))
        {
            ActiveTab = Tabs.ElementAtOrDefault(Math.Min(index, Tabs.Count - 1));
        }
    }
}
