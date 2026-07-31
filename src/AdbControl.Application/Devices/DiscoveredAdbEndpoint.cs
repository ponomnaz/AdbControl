namespace AdbControl.Application.Devices;

public sealed record DiscoveredAdbEndpoint(
    string Address,
    int Port,
    string Endpoint,
    string NetworkLabel,
    TimeSpan ResponseTime,
    AdbEndpointState State,
    string? Model)
{
    public bool IsAdb => State is AdbEndpointState.AdbReady
        or AdbEndpointState.AdbUnauthorized
        or AdbEndpointState.AdbTlsRequired;
}
