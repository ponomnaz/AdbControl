using System.Windows.Input;
using AdbControl.Application.Common;
using AdbControl.Application.Workspace;
using AdbControl.Core.Tools;

namespace AdbControl.Tools.Library.ViewModels;

public sealed class ToolLibraryViewModel
{
    public ToolLibraryViewModel(ToolActivationContext context)
    {
        ToolCards = context.ToolCatalog.Registrations
            .Select(x => new ToolCardViewModel(
                x.Tool.Title,
                x.Tool.Description,
                x.Module.Name,
                GetCategoryLabel(x.Tool.Category),
                GetScopeLabel(x.Tool.TargetScope),
                x.Tool.Glyph,
                new RelayCommand(() => context.OpenTool(x.Tool.Id))))
            .ToArray();
    }

    public IReadOnlyList<ToolCardViewModel> ToolCards { get; }

    private static string GetCategoryLabel(ToolCategory category) =>
        category switch
        {
            ToolCategory.Workspace => "Рабочее место",
            ToolCategory.Devices => "Устройства",
            ToolCategory.Operations => "Операции",
            ToolCategory.Diagnostics => "Диагностика",
            ToolCategory.Data => "Данные",
            _ => "Другое"
        };

    private static string GetScopeLabel(ToolTargetScope scope) =>
        scope switch
        {
            ToolTargetScope.None => "Без привязки",
            ToolTargetScope.SingleDevice => "Одно устройство",
            ToolTargetScope.MultiDevice => "Несколько устройств",
            _ => "Не задано"
        };
}

public sealed record ToolCardViewModel(
    string Title,
    string Description,
    string ModuleName,
    string Category,
    string TargetScope,
    string Glyph,
    ICommand OpenCommand);
