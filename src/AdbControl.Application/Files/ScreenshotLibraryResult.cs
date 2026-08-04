namespace AdbControl.Application.Files;

/// <summary>
/// Итог файловой операции. Отказ — это обычный результат с текстом для пользователя,
/// а не исключение: занятый файл и совпадение имён случаются постоянно.
/// </summary>
public sealed record ScreenshotLibraryResult(
    bool IsSuccess,
    string Message,
    IReadOnlyList<string> Paths)
{
    public static ScreenshotLibraryResult Success(string message, params string[] paths)
    {
        return new ScreenshotLibraryResult(true, message, paths);
    }

    public static ScreenshotLibraryResult Failure(string message)
    {
        return new ScreenshotLibraryResult(false, message, []);
    }
}
