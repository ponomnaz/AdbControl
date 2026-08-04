namespace AdbControl.Application.Files;

/// <summary>
/// Файловые операции над папкой снимков. Всё, что снаружи корня, отклоняется здесь,
/// а не в интерфейсе: вкладка не должна быть последней защитой от удаления чужого.
/// </summary>
public interface IScreenshotLibraryService
{
    /// <summary>Корень, за пределы которого ни одна операция не выходит.</summary>
    string RootDirectory { get; }

    ScreenshotLibraryResult CreateFolder(string parentDirectory, string name);

    /// <summary>Переименование. Расширение файла сохраняется само — вводится только имя.</summary>
    ScreenshotLibraryResult Rename(string fullPath, string newName);

    ScreenshotLibraryResult Move(IReadOnlyList<string> fullPaths, string targetDirectory);

    /// <summary>Удаление в корзину Windows: вернуть файл можно штатными средствами.</summary>
    ScreenshotLibraryResult Delete(IReadOnlyList<string> fullPaths);
}
