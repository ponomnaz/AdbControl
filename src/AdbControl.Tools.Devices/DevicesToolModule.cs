using AdbControl.Application.Tools;
using AdbControl.Core.Modules;
using AdbControl.Core.Tools;
using AdbControl.Core.Workspace;
using AdbControl.Tools.Devices.ViewModels;

namespace AdbControl.Tools.Devices;

public sealed class DevicesToolModule : IToolModule
{
    public ModuleDescriptor Module { get; } = new(
        "devices",
        "Устройства",
        "Список телевизоров и выбор активных устройств.",
        SortOrder: 10);

    public IEnumerable<ToolRegistration> GetToolRegistrations()
    {
        yield return new ToolRegistration(
            Module,
            new ToolDescriptor(
                "devices",
                "Устройства",
                "Подключенные устройства и быстрые действия.",
                ToolCategory.Devices,
                ToolTargetScope.MultiDevice,
                WorkspaceHost.MainTab,
                "TV",
                SortOrder: 0),
            context => new DevicesToolViewModel(
                context.DeviceInventory,
                context.DeviceActions,
                context.DeviceAliases,
                context.NetariumServerEndpoint),
            ShowInNavigation: true,
            OpenOnStartup: true,
            ReuseExistingTab: true);

        yield return new ToolRegistration(
            Module,
            new ToolDescriptor(
                "device-connect",
                "Подключение",
                "Поиск устройств в сети и подключение.",
                ToolCategory.Devices,
                ToolTargetScope.MultiDevice,
                WorkspaceHost.MainTab,
                "+",
                SortOrder: 10),
            context => new DeviceConnectToolViewModel(
                context.DeviceInventory,
                context.DeviceDiscovery,
                context.MdnsDiscovery,
                context.AdbConnection,
                context.DeviceAliases,
                context.AutoConnectDevices,
                context.ScanProfiles),
            ShowInNavigation: true,
            OpenOnStartup: false,
            ReuseExistingTab: true);
    }

    public IEnumerable<Uri> GetResourceDictionaryUris()
    {
        yield return new Uri("/AdbControl.Tools.Devices;component/Themes/DevicesTemplates.xaml", UriKind.Relative);
    }
}
