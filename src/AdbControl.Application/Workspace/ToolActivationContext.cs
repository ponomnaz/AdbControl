using AdbControl.Application.Devices;
using AdbControl.Application.Tools;

namespace AdbControl.Application.Workspace;

public sealed record ToolActivationContext(
    ToolCatalog ToolCatalog,
    DeviceInventoryState DeviceInventory,
    IDeviceDiscoveryService DeviceDiscovery,
    IAdbConnectionService AdbConnection,
    IDeviceActionService DeviceActions,
    Action<string> OpenTool);
