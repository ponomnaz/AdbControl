using AdbControl.Application.Common;
using System.Windows.Input;

namespace AdbControl.Shell.ViewModels;

public sealed class ToolNavigationItemViewModel : ObservableObject
{
    private bool _isActive;

    public required string Id { get; init; }

    public required string Title { get; init; }

    public required string Description { get; init; }

    public required string Glyph { get; init; }

    public required ICommand OpenCommand { get; init; }

    public bool IsActive
    {
        get => _isActive;
        set => SetProperty(ref _isActive, value);
    }
}
