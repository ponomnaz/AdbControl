namespace AdbControl.Application.Devices;

/// <summary>
/// Насколько подтверждено, что за адресом действительно ADB.
/// </summary>
public enum AdbEndpointState
{
    /// <summary>Состояние не проверялось (например, endpoint взят из <c>adb devices</c>).</summary>
    Unknown,

    /// <summary>TCP-порт открыт, но на ADB-рукопожатие адрес не ответил.</summary>
    PortOpen,

    /// <summary>ADB отвечает и готов к работе.</summary>
    AdbReady,

    /// <summary>ADB отвечает, но требует подтверждения отладки на экране устройства.</summary>
    AdbUnauthorized,

    /// <summary>ADB отвечает и требует TLS (Android 11+, беспроводная отладка).</summary>
    AdbTlsRequired,

    /// <summary>
    /// Устройство объявило службу сопряжения и ждёт шестизначный код.
    /// Получено из mDNS, а не из рукопожатия: порт сопряжения пробовать бессмысленно.
    /// </summary>
    PairingRequired
}
