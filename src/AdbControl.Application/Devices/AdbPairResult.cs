namespace AdbControl.Application.Devices;

public sealed record AdbPairResult(string Endpoint, bool IsSuccess, string Message);
