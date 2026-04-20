namespace AdbControl.Application.Devices;

public sealed record AdbDisconnectResult(
    string Endpoint,
    bool IsSuccess,
    string Message);
