using AdbControl.Application.Tools;
using AdbControl.Core.Modules;
using AdbControl.Core.Tools;
using AdbControl.Core.Workspace;
using AdbControl.Tools.Home.ViewModels;

namespace AdbControl.Tools.Home;

public sealed class HomeToolModule : IToolModule
{
    public ModuleDescriptor Module { get; } = new(
        "home",
        "Главная",
        "Стартовый раздел приложения.",
        SortOrder: 0);

    public IEnumerable<ToolRegistration> GetToolRegistrations()
    {
        yield return new ToolRegistration(
            Module,
            new ToolDescriptor(
                "home",
                "Главная",
                "Стартовый экран и вход в основные разделы.",
                ToolCategory.Workspace,
                ToolTargetScope.None,
                WorkspaceHost.MainTab,
                "⌂",
                SortOrder: 0),
            context => new HomeToolViewModel(context),
            ShowInNavigation: false,
            OpenOnStartup: false,
            ReuseExistingTab: true);
    }

    public IEnumerable<Uri> GetResourceDictionaryUris()
    {
        yield return new Uri("/AdbControl.Tools.Home;component/Themes/HomeTemplates.xaml", UriKind.Relative);
    }
}
