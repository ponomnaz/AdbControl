namespace AdbControl.Application.Logcat;

public interface IDeviceLogcatSession : IAsyncDisposable
{
    Task StopAsync(CancellationToken cancellationToken = default);
}
