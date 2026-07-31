using AdbControl.Core.Devices;

namespace AdbControl.Application.Devices;

public enum AdbDeviceState
{
    Unknown,

    /// <summary>Готово к работе.</summary>
    Device,

    /// <summary>Транспорт есть, но устройство не отвечает.</summary>
    Offline,

    /// <summary>Нужно подтвердить отладку на экране устройства.</summary>
    Unauthorized
}

/// <summary>
/// Строка вывода <c>adb devices</c>. В отличие от прежнего списка endpoint'ов,
/// сюда попадают и USB-устройства, у которых серийник вместо адреса.
/// </summary>
/// <param name="IsConnectableEndpoint">
/// Серийник имеет вид <c>ip:порт</c>, то есть к нему применим <c>adb connect</c>.
/// Беспроводная отладка Android 11+ этого не даёт: там транспорт называется
/// <c>adb-SERIAL-XXXX._adb-tls-connect._tcp</c> — сетевой по природе, но подключаться
/// по такому имени нельзя, только выбирать уже существующий транспорт.
/// </param>
public sealed record AdbDeviceEntry(
    string Serial,
    DeviceConnectionKind Kind,
    AdbDeviceState State,
    bool IsConnectableEndpoint)
{
    public bool IsReady => State == AdbDeviceState.Device;

    public bool IsNetwork => Kind == DeviceConnectionKind.Network;
}
