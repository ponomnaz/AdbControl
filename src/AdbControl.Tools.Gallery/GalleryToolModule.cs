using AdbControl.Application.Tools;
using AdbControl.Core.Modules;
using AdbControl.Core.Tools;
using AdbControl.Core.Workspace;
using AdbControl.Tools.Gallery.ViewModels;

namespace AdbControl.Tools.Gallery;

public sealed class GalleryToolModule : IToolModule
{
    public ModuleDescriptor Module { get; } = new(
        "gallery",
        "Скриншоты",
        "Снимки экрана устройств: просмотр и раскладка по папкам.",
        SortOrder: 26);

    public IEnumerable<ToolRegistration> GetToolRegistrations()
    {
        yield return new ToolRegistration(
            Module,
            new ToolDescriptor(
                "screenshot-gallery",
                "Скриншоты",
                "Просмотр снятых экранов и раскладка их по папкам.",
                ToolCategory.Data,
                ToolTargetScope.None,
                WorkspaceHost.MainTab,
                "IMG",
                SortOrder: 40),
            context => new ScreenshotGalleryToolViewModel(context.ScreenshotLibrary, context.GallerySettings),
            ShowInNavigation: true,
            OpenOnStartup: false,
            ReuseExistingTab: true);
    }

    public IEnumerable<Uri> GetResourceDictionaryUris()
    {
        yield return new Uri("/AdbControl.Tools.Gallery;component/Themes/GalleryTemplates.xaml", UriKind.Relative);
    }
}
