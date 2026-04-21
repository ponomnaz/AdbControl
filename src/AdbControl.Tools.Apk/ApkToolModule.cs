using AdbControl.Application.Tools;
using AdbControl.Core.Modules;
using AdbControl.Core.Tools;
using AdbControl.Core.Workspace;
using AdbControl.Tools.Apk.ViewModels;

namespace AdbControl.Tools.Apk;

public sealed class ApkToolModule : IToolModule
{
    public ModuleDescriptor Module { get; } = new(
        "apk",
        "APK",
        "Локальная библиотека APK-файлов.",
        SortOrder: 15);

    public IEnumerable<ToolRegistration> GetToolRegistrations()
    {
        yield return new ToolRegistration(
            Module,
            new ToolDescriptor(
                "apk-library",
                "APK",
                "Импорт, хранение и удаление локальных APK.",
                ToolCategory.Data,
                ToolTargetScope.None,
                WorkspaceHost.MainTab,
                "APK",
                SortOrder: 12),
            context => new ApkLibraryToolViewModel(context.ApkLibrary, context.DeviceInventory),
            ShowInNavigation: true,
            OpenOnStartup: false,
            ReuseExistingTab: true);
    }

    public IEnumerable<Uri> GetResourceDictionaryUris()
    {
        yield return new Uri("/AdbControl.Tools.Apk;component/Themes/ApkTemplates.xaml", UriKind.Relative);
    }
}
