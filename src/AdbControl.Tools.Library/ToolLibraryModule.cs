using AdbControl.Application.Tools;
using AdbControl.Core.Modules;
using AdbControl.Core.Tools;
using AdbControl.Core.Workspace;
using AdbControl.Tools.Library.ViewModels;

namespace AdbControl.Tools.Library;

public sealed class ToolLibraryModule : IToolModule
{
    public ModuleDescriptor Module { get; } = new(
        "tool-library",
        "Разделы",
        "Список доступных экранов приложения.",
        SortOrder: 20);

    public IEnumerable<ToolRegistration> GetToolRegistrations()
    {
        yield return new ToolRegistration(
            Module,
            new ToolDescriptor(
                "tool-library",
                "Разделы",
                "Все доступные экраны приложения.",
                ToolCategory.Workspace,
                ToolTargetScope.None,
                WorkspaceHost.MainTab,
                "RD",
                SortOrder: 10),
            context => new ToolLibraryViewModel(context),
            ShowInNavigation: false,
            OpenOnStartup: false,
            ReuseExistingTab: true);
    }

    public IEnumerable<Uri> GetResourceDictionaryUris()
    {
        yield return new Uri("/AdbControl.Tools.Library;component/Themes/ToolLibraryTemplates.xaml", UriKind.Relative);
    }
}
