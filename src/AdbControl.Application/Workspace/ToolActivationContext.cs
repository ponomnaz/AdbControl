using AdbControl.Application.Apk;
using AdbControl.Application.Devices;
using AdbControl.Application.Diagnostics;
using AdbControl.Application.Files;
using AdbControl.Application.Logcat;
using AdbControl.Application.Remote;
using AdbControl.Application.Terminal;
using AdbControl.Application.Top;
using AdbControl.Application.Tools;

namespace AdbControl.Application.Workspace;

public sealed record ToolActivationContext(
    ToolCatalog ToolCatalog,
    DeviceInventoryState DeviceInventory,
    DeviceAliasCatalog DeviceAliases,
    NetariumServerEndpointCatalog NetariumServerEndpoint,
    AutoConnectDeviceCatalog AutoConnectDevices,
    ScanProfileCatalog ScanProfiles,
    IApkLibraryService ApkLibrary,
    IApkDeploymentService ApkDeployment,
    IApkDevicePackageService ApkDevicePackages,
    IDeviceDiscoveryService DeviceDiscovery,
    IMdnsDiscoveryService MdnsDiscovery,
    DeviceInventorySyncService DeviceInventorySync,
    IAdbConnectionService AdbConnection,
    IDeviceActionService DeviceActions,
    IDeviceScreenshotService DeviceScreenshots,
    IScreenshotLibraryService ScreenshotLibrary,
    IGallerySettingsStore GallerySettings,
    IDeviceLogcatService DeviceLogcat,
    LogcatSettingsCatalog LogcatSettings,
    IAdbConsoleService AdbConsole,
    IDeviceTopService DeviceTop,
    IRemoteControlService RemoteControl,
    CommandTraceJournal CommandTraceJournal,
    Action<string> OpenTool);
