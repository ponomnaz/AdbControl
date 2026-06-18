using AdbControl.Application.Tools;
using AdbControl.Core.Modules;
using AdbControl.Core.Tools;
using AdbControl.Core.Workspace;
using AdbControl.Tools.Remote.ViewModels;

namespace AdbControl.Tools.Remote;

public sealed class RemoteToolModule : IToolModule
{
    public ModuleDescriptor Module { get; } = new(
        "remote",
        "Пульт",
        "Виртуальный пульт и трансляция экрана Android TV.",
        SortOrder: 18);

    public IEnumerable<ToolRegistration> GetToolRegistrations()
    {
        yield return new ToolRegistration(
            Module,
            new ToolDescriptor(
                "remote-control",
                "Пульт",
                "Виртуальный пульт и трансляция экрана выбранного телевизора.",
                ToolCategory.Diagnostics,
                ToolTargetScope.None,
                WorkspaceHost.MainTab,
                "⊞",
                SortOrder: 18),
            context => new RemoteViewModel(
                context.DeviceInventory,
                context.DeviceAliases,
                context.RemoteControl),
            ShowInNavigation: true,
            OpenOnStartup: false,
            ReuseExistingTab: true);
    }

    public IEnumerable<Uri> GetResourceDictionaryUris()
    {
        yield return new Uri("/AdbControl.Tools.Remote;component/Themes/RemoteTemplates.xaml", UriKind.Relative);
    }
}
