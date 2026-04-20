namespace AdbControl.Application.Devices;

public sealed record AdbConnectResult(
    string Endpoint,
    bool IsSuccess,
    string Message);
