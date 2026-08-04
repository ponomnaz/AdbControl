namespace AdbControl.Application.Files;

/// <summary>
/// Что вкладка «Скриншоты» помнит между запусками. Ключ сортировки хранится строкой:
/// перечисление живёт в слое вкладки, и тянуть его сюда ради двух настроек незачем.
/// </summary>
public sealed record GallerySettings(string SortKey, bool SortDescending, double ListWidth)
{
    public static GallerySettings Default { get; } = new("taken", true, 300);
}
