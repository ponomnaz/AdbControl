using AdbControl.Application.Common;
using AdbControl.Application.Tools;

namespace AdbControl.Application.Workspace;

public sealed class WorkspaceTab : ObservableObject
{
    private bool _isSelected;

    public WorkspaceTab(ToolRegistration registration, object contentViewModel)
    {
        Id = Guid.NewGuid();
        Registration = registration;
        ContentViewModel = contentViewModel;
    }

    public Guid Id { get; }

    public ToolRegistration Registration { get; }

    public string Title => Registration.Tool.Title;

    public string Description => Registration.Tool.Description;

    public object ContentViewModel { get; }

    public bool IsSelected
    {
        get => _isSelected;
        set => SetProperty(ref _isSelected, value);
    }
}
