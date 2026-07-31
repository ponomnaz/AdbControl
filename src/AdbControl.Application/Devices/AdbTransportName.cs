namespace AdbControl.Application.Devices;

public static class AdbTransportName
{
    /// <summary>
    /// Человекочитаемое имя транспорта. Беспроводная отладка Android 11+ называет транспорт
    /// <c>adb-СЕРИЙНИК-суффикс._adb-tls-connect._tcp</c> — в списке от такой строки толку нет,
    /// поэтому оставляем серийник. Остальные идентификаторы возвращаются как есть.
    /// </summary>
    public static string Describe(string deviceId)
    {
        if (string.IsNullOrWhiteSpace(deviceId))
        {
            return deviceId;
        }

        var dotIndex = deviceId.IndexOf('.');
        if (dotIndex <= 0 || !deviceId.StartsWith("adb-", StringComparison.OrdinalIgnoreCase))
        {
            return deviceId;
        }

        var instanceName = deviceId[4..dotIndex];
        var suffixIndex = instanceName.LastIndexOf('-');

        return suffixIndex > 0 ? instanceName[..suffixIndex] : instanceName;
    }

    /// <summary>Транспорт mDNS: сетевой, но <c>adb connect</c> по такому имени невозможен.</summary>
    public static bool IsMdnsTransport(string deviceId)
    {
        return deviceId.Contains("._tcp", StringComparison.OrdinalIgnoreCase);
    }
}
