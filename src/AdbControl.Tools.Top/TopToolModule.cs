using AdbControl.Application.Tools;
using AdbControl.Core.Modules;
using AdbControl.Core.Tools;
using AdbControl.Core.Workspace;
using AdbControl.Tools.Top.ViewModels;

namespace AdbControl.Tools.Top;

public sealed class TopToolModule : IToolModule
{
    public ModuleDescriptor Module { get; } = new(
        "top",
        "Top",
        "Нагрузка процессов на выбранном устройстве.",
        SortOrder: 20);

    public IEnumerable<ToolRegistration> GetToolRegistrations()
    {
        yield return new ToolRegistration(
            Module,
            new ToolDescriptor(
                "device-top",
                "Top",
                "Снимок процессов и нагрузки выбранного устройства.",
                ToolCategory.Diagnostics,
                ToolTargetScope.SingleDevice,
                WorkspaceHost.MainTab,
                "TOP",
                SortOrder: 15),
            context => new DeviceTopToolViewModel(context.DeviceInventory, context.DeviceAliases, context.DeviceTop),
            ShowInNavigation: true,
            OpenOnStartup: false,
            ReuseExistingTab: true);
    }

    public IEnumerable<Uri> GetResourceDictionaryUris()
    {
        yield return new Uri("/AdbControl.Tools.Top;component/Themes/TopTemplates.xaml", UriKind.Relative);
    }
}
