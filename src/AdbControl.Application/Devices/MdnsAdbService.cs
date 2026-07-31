namespace AdbControl.Application.Devices;

public enum MdnsServiceKind
{
    /// <summary>Неизвестный тип службы.</summary>
    Unknown,

    /// <summary><c>_adb._tcp</c> — классический ADB по сети.</summary>
    Legacy,

    /// <summary><c>_adb-tls-connect._tcp</c> — беспроводная отладка Android 11+, готова к подключению.</summary>
    Connect,

    /// <summary><c>_adb-tls-pairing._tcp</c> — устройство ждёт сопряжения по коду.</summary>
    Pairing
}

/// <summary>
/// Служба ADB, объявленная устройством через mDNS. Это единственный способ узнать
/// случайный порт беспроводной отладки Android 11+, не перебирая 35 тысяч портов.
/// </summary>
public sealed record MdnsAdbService(
    string InstanceName,
    MdnsServiceKind Kind,
    string Host,
    int Port)
{
    public string Endpoint => $"{Host}:{Port}";
}
