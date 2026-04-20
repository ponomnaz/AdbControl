namespace AdbControl.Application.Devices;

public sealed record DiscoveredAdbEndpoint(
    string Address,
    string Endpoint,
    string NetworkLabel);
