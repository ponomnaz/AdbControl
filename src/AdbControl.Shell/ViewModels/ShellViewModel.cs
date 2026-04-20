using System.Collections.Specialized;
using AdbControl.Application.Common;
using AdbControl.Application.Devices;
using AdbControl.Application.Tools;
using AdbControl.Application.Workspace;

namespace AdbControl.Shell.ViewModels;

public sealed class ShellViewModel : ObservableObject
{
    private readonly DeviceInventoryState _deviceInventory;
    private readonly WorkspaceService _workspace;

    public ShellViewModel(ToolCatalog toolCatalog, WorkspaceService workspace, DeviceInventoryState deviceInventory)
    {
        _workspace = workspace;
        _deviceInventory = deviceInventory;

        NavigationItems = toolCatalog.NavigationTools
            .Select(x => new ToolNavigationItemViewModel
            {
                Id = x.Tool.Id,
                Title = x.Tool.Title,
                Description = x.Tool.Description,
                Glyph = x.Tool.Glyph,
                OpenCommand = new RelayCommand(() => _workspace.OpenTool(x.Tool.Id))
            })
            .ToArray();

        _workspace.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName is nameof(WorkspaceService.ActiveTab))
            {
                UpdateNavigationState();
            }
        };

        _workspace.Tabs.CollectionChanged += OnTabsChanged;
        _deviceInventory.KnownDevices.CollectionChanged += OnDevicesChanged;
        _deviceInventory.SelectedDevices.CollectionChanged += OnDevicesChanged;

        UpdateNavigationState();
    }

    public IReadOnlyList<ToolNavigationItemViewModel> NavigationItems { get; }

    public WorkspaceService Workspace => _workspace;

    public string SelectionSummary => _deviceInventory.SelectionSummary;

    public string DeviceSummary =>
        _deviceInventory.KnownDevices.Count switch
        {
            0 => "Сохраненных телевизоров пока нет",
            1 => "В списке 1 телевизор",
            _ => $"Телевизоров в списке: {_deviceInventory.KnownDevices.Count}"
        };

    public string WorkspaceSummary =>
        _workspace.Tabs.Count switch
        {
            0 => "Нет открытых вкладок",
            1 => "Открыта 1 вкладка",
            _ => $"Открыто вкладок: {_workspace.Tabs.Count}"
        };

    public string ActivitySummary =>
        "Фоновые операции и логи пока не добавлены.";

    private void OnTabsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        OnPropertyChanged(nameof(WorkspaceSummary));
        UpdateNavigationState();
    }

    private void OnDevicesChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        OnPropertyChanged(nameof(SelectionSummary));
        OnPropertyChanged(nameof(DeviceSummary));
    }

    private void UpdateNavigationState()
    {
        var activeToolId = _workspace.ActiveTab?.Registration.Tool.Id;
        foreach (var navigationItem in NavigationItems)
        {
            navigationItem.IsActive = string.Equals(navigationItem.Id, activeToolId, StringComparison.OrdinalIgnoreCase);
        }
    }
}
