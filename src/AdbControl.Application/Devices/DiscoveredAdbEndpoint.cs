namespace AdbControl.Application.Devices;

public sealed record DiscoveredAdbEndpoint(
    string Address,
    int Port,
    string Endpoint,
    string NetworkLabel,
    TimeSpan ResponseTime);
