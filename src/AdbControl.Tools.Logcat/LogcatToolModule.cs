using AdbControl.Application.Tools;
using AdbControl.Core.Modules;
using AdbControl.Core.Tools;
using AdbControl.Core.Workspace;
using AdbControl.Tools.Logcat.ViewModels;

namespace AdbControl.Tools.Logcat;

public sealed class LogcatToolModule : IToolModule
{
    public ModuleDescriptor Module { get; } = new(
        "logcat",
        "Logcat",
        "Поток логов выбранного устройства с фильтрами и поиском.",
        SortOrder: 22);

    public IEnumerable<ToolRegistration> GetToolRegistrations()
    {
        yield return new ToolRegistration(
            Module,
            new ToolDescriptor(
                "device-logcat",
                "Logcat",
                "Логи выбранного устройства в реальном времени.",
                ToolCategory.Diagnostics,
                ToolTargetScope.SingleDevice,
                WorkspaceHost.MainTab,
                "LOG",
                SortOrder: 16),
            context => new DeviceLogcatToolViewModel(context.DeviceInventory, context.DeviceAliases, context.DeviceLogcat),
            ShowInNavigation: true,
            OpenOnStartup: false,
            ReuseExistingTab: true);
    }

    public IEnumerable<Uri> GetResourceDictionaryUris()
    {
        yield return new Uri("/AdbControl.Tools.Logcat;component/Themes/LogcatTemplates.xaml", UriKind.Relative);
    }
}
