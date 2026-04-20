namespace AdbControl.Core.Devices;

public sealed record TvDeviceProfile(
    string Id,
    string DisplayName,
    string? NetworkEndpoint,
    DeviceConnectionKind PreferredConnection,
    DeviceReachability Reachability);
