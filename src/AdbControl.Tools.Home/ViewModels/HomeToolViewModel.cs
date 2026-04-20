using System.Windows.Input;
using AdbControl.Application.Common;
using AdbControl.Application.Workspace;

namespace AdbControl.Tools.Home.ViewModels;

public sealed class HomeToolViewModel
{
    public HomeToolViewModel(ToolActivationContext context)
    {
        OpenDevicesCommand = new RelayCommand(() => context.OpenTool("devices"));
        OpenToolLibraryCommand = new RelayCommand(() => context.OpenTool("tool-library"));

        PrimaryLinks =
        [
            new HomeJumpLink(
                "Устройства",
                "Список телевизоров и выбор активных устройств.",
                "TV",
                OpenDevicesCommand),
            new HomeJumpLink(
                "Разделы",
                "Все доступные экраны приложения в одном месте.",
                "RD",
                OpenToolLibraryCommand)
        ];

        StatusText = "Пока здесь только каркас интерфейса. Дальше на этой вкладке появятся избранные действия и пресеты.";
    }

    public IReadOnlyList<HomeJumpLink> PrimaryLinks { get; }

    public ICommand OpenDevicesCommand { get; }

    public ICommand OpenToolLibraryCommand { get; }

    public string StatusText { get; }
}

public sealed record HomeJumpLink(
    string Title,
    string Description,
    string Glyph,
    ICommand OpenCommand);
