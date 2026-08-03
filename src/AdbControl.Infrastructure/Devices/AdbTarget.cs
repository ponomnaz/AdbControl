using AdbControl.Core.Devices;

namespace AdbControl.Infrastructure.Devices;

internal static class AdbTarget
{
    /// <summary>
    /// Цель для <c>adb -s</c>. У USB-устройств и mDNS-транспортов беспроводной отладки
    /// сетевого адреса нет — для них целью служит идентификатор.
    /// </summary>
    public static string? Resolve(TvDeviceProfile device)
    {
        if (!string.IsNullOrWhiteSpace(device.NetworkEndpoint))
        {
            return device.NetworkEndpoint;
        }

        return string.IsNullOrWhiteSpace(device.Id) ? null : device.Id;
    }
}
