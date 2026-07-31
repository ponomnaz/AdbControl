namespace AdbControl.Application.Devices;

/// <summary>
/// Профиль хранится в том же виде, в каком его набирают в полях «Сеть» и «Порты».
/// Так он переживает изменения модели целей и остаётся читаемым в JSON-файле руками.
/// </summary>
public sealed record SavedScanProfile(string Name, string Targets, string Ports);
