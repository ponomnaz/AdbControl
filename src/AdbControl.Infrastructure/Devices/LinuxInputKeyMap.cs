using AdbControl.Application.Remote;

namespace AdbControl.Infrastructure.Devices;

/// <summary>
/// Коды клавиш ядра Linux для записи в <c>/dev/input</c>. Это не коды Android: там,
/// куда пишет <c>sendevent</c>, живёт слой драйвера, и «вниз» там 108, а не 20.
/// Соответствие кодов ядра андроидным задаёт раскладка устройства, и для обычных
/// клавиш пульта она стандартная.
/// </summary>
internal static class LinuxInputKeyMap
{
    private static readonly Dictionary<int, int> Codes = new()
    {
        [RemoteKeyCode.DpadUp] = 103,
        [RemoteKeyCode.DpadDown] = 108,
        [RemoteKeyCode.DpadLeft] = 105,
        [RemoteKeyCode.DpadRight] = 106,
        [RemoteKeyCode.DpadCenter] = 28,   // KEY_ENTER
        [RemoteKeyCode.Home] = 172,        // KEY_HOMEPAGE
        [RemoteKeyCode.Back] = 158,        // KEY_BACK
        [RemoteKeyCode.Menu] = 139,        // KEY_MENU
        [RemoteKeyCode.Power] = 116,       // KEY_POWER
        [RemoteKeyCode.VolumeUp] = 115,
        [RemoteKeyCode.VolumeDown] = 114,
        [RemoteKeyCode.VolumeMute] = 113,
        [RemoteKeyCode.MediaPlayPause] = 164,
        [RemoteKeyCode.MediaRewind] = 168,
        [RemoteKeyCode.MediaFastForward] = 208,
        [RemoteKeyCode.MediaStop] = 166,

        // Цифры в ядре идут подряд с единицы, ноль стоит последним.
        [RemoteKeyCode.Digit1] = 2,
        [RemoteKeyCode.Digit2] = 3,
        [RemoteKeyCode.Digit3] = 4,
        [RemoteKeyCode.Digit4] = 5,
        [RemoteKeyCode.Digit5] = 6,
        [RemoteKeyCode.Digit6] = 7,
        [RemoteKeyCode.Digit7] = 8,
        [RemoteKeyCode.Digit8] = 9,
        [RemoteKeyCode.Digit9] = 10,
        [RemoteKeyCode.Digit0] = 11
    };

    /// <summary>Коды, без которых быстрый путь бессмысленен: по ним и выбираем устройство.</summary>
    public static readonly int[] RequiredCodes = [103, 108, 105, 106, 28];

    public static bool TryGetLinuxCode(int androidKeyCode, out int linuxCode)
    {
        return Codes.TryGetValue(androidKeyCode, out linuxCode);
    }
}
