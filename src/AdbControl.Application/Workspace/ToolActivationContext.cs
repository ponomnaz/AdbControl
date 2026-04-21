using AdbControl.Application.Apk;
using AdbControl.Application.Devices;
using AdbControl.Application.Diagnostics;
using AdbControl.Application.Top;
using AdbControl.Application.Tools;

namespace AdbControl.Application.Workspace;

public sealed record ToolActivationContext(
    ToolCatalog ToolCatalog,
    DeviceInventoryState DeviceInventory,
    DeviceAliasCatalog DeviceAliases,
    IApkLibraryService ApkLibrary,
    IApkDeploymentService ApkDeployment,
    IDeviceDiscoveryService DeviceDiscovery,
    IAdbConnectionService AdbConnection,
    IDeviceActionService DeviceActions,
    IDeviceTopService DeviceTop,
    CommandTraceJournal CommandTraceJournal,
    Action<string> OpenTool);
