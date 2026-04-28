using AdbControl.Application.Tools;
using AdbControl.Core.Modules;
using AdbControl.Core.Tools;
using AdbControl.Core.Workspace;
using AdbControl.Tools.Console.ViewModels;

namespace AdbControl.Tools.Console;

public sealed class ConsoleToolModule : IToolModule
{
    public ModuleDescriptor Module { get; } = new(
        "console",
        "Консоль",
        "Ручной запуск ADB-команд.",
        SortOrder: 24);

    public IEnumerable<ToolRegistration> GetToolRegistrations()
    {
        yield return new ToolRegistration(
            Module,
            new ToolDescriptor(
                "adb-console",
                "Консоль",
                "Ручной ввод ADB-команд и просмотр вывода.",
                ToolCategory.Diagnostics,
                ToolTargetScope.None,
                WorkspaceHost.MainTab,
                "CLI",
                SortOrder: 17),
            context => new AdbConsoleToolViewModel(context.AdbConsole),
            ShowInNavigation: true,
            OpenOnStartup: false,
            ReuseExistingTab: true);
    }

    public IEnumerable<Uri> GetResourceDictionaryUris()
    {
        yield return new Uri("/AdbControl.Tools.Console;component/Themes/ConsoleTemplates.xaml", UriKind.Relative);
    }
}
