namespace AdbControl.Application.Remote;

public interface IRemoteControlService
{
    Task<bool> SendKeyEventAsync(string targetId, int keyCode, CancellationToken cancellationToken = default);

    /// <summary>Opens a persistent "adb shell" session for the device so subsequent key events avoid per-call connection overhead.</summary>
    Task StartSessionAsync(string targetId, CancellationToken cancellationToken = default);

    /// <summary>Closes the persistent session opened by <see cref="StartSessionAsync"/>, if any.</summary>
    void StopSession();
}
