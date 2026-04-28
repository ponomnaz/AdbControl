using System.Collections.Specialized;
using AdbControl.Application.Common;
using AdbControl.Application.Devices;
using AdbControl.Application.Diagnostics;
using AdbControl.Application.Tools;
using AdbControl.Application.Workspace;

namespace AdbControl.Shell.ViewModels;

public sealed class ShellViewModel : ObservableObject
{
    private const string CommandLogToolId = "command-log";
    private readonly CommandTraceJournal _commandTraceJournal;
    private readonly DeviceInventoryState _deviceInventory;
    private readonly WorkspaceService _workspace;

    public ShellViewModel(
        ToolCatalog toolCatalog,
        WorkspaceService workspace,
        DeviceInventoryState deviceInventory,
        CommandTraceJournal commandTraceJournal)
    {
        _workspace = workspace;
        _deviceInventory = deviceInventory;
        _commandTraceJournal = commandTraceJournal;

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
        _commandTraceJournal.PropertyChanged += OnCommandTraceChanged;

        UpdateNavigationState();
        UpdateAttentionState();
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

    public DetachedToolContent CreateDetachedToolContent(string toolId)
    {
        return _workspace.CreateDetachedToolContent(toolId);
    }

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

    private void OnCommandTraceChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(CommandTraceJournal.UnreadErrorCount) or nameof(CommandTraceJournal.HasUnreadErrors))
        {
            UpdateAttentionState();
        }
    }

    private void UpdateNavigationState()
    {
        var activeToolId = _workspace.ActiveTab?.Registration.Tool.Id;
        if (string.Equals(activeToolId, CommandLogToolId, StringComparison.OrdinalIgnoreCase))
        {
            _commandTraceJournal.MarkErrorsAsViewed();
        }

        foreach (var navigationItem in NavigationItems)
        {
            navigationItem.IsActive = string.Equals(navigationItem.Id, activeToolId, StringComparison.OrdinalIgnoreCase);
        }

        UpdateAttentionState();
    }

    private void UpdateAttentionState()
    {
        foreach (var navigationItem in NavigationItems)
        {
            navigationItem.AttentionCount = string.Equals(navigationItem.Id, CommandLogToolId, StringComparison.OrdinalIgnoreCase)
                ? _commandTraceJournal.UnreadErrorCount
                : 0;
        }
    }
}
