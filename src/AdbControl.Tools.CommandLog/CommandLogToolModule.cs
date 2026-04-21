using AdbControl.Application.Tools;
using AdbControl.Core.Modules;
using AdbControl.Core.Tools;
using AdbControl.Core.Workspace;
using AdbControl.Tools.CommandLog.ViewModels;

namespace AdbControl.Tools.CommandLog;

public sealed class CommandLogToolModule : IToolModule
{
    public ModuleDescriptor Module { get; } = new(
        "command-log",
        "Журнал",
        "ADB-команды, вывод и ошибки.",
        SortOrder: 30);

    public IEnumerable<ToolRegistration> GetToolRegistrations()
    {
        yield return new ToolRegistration(
            Module,
            new ToolDescriptor(
                "command-log",
                "Журнал",
                "Команды, время выполнения и результат.",
                ToolCategory.Diagnostics,
                ToolTargetScope.None,
                WorkspaceHost.MainTab,
                ">_",
                SortOrder: 20),
            context => new CommandLogToolViewModel(context.CommandTraceJournal, context.DeviceAliases),
            ShowInNavigation: true,
            OpenOnStartup: false,
            ReuseExistingTab: true);
    }

    public IEnumerable<Uri> GetResourceDictionaryUris()
    {
        yield return new Uri("/AdbControl.Tools.CommandLog;component/Themes/CommandLogTemplates.xaml", UriKind.Relative);
    }
}
