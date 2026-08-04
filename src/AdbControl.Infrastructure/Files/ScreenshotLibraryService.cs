using System.Runtime.Versioning;
using AdbControl.Application.Files;
using Microsoft.VisualBasic.FileIO;

namespace AdbControl.Infrastructure.Files;

[SupportedOSPlatform("windows")]
public sealed class ScreenshotLibraryService : IScreenshotLibraryService
{
    private const int MaxNameLength = 200;

    /// <summary>Имена устройств DOS: Windows не даст создать такой файл, а ошибку выдаст невнятную.</summary>
    private static readonly HashSet<string> ReservedNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "CON", "PRN", "AUX", "NUL",
        "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
        "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9"
    };

    public ScreenshotLibraryService(string rootDirectory)
    {
        RootDirectory = rootDirectory;
    }

    public string RootDirectory { get; }

    public ScreenshotLibraryResult CreateFolder(string parentDirectory, string name)
    {
        if (!IsInsideRoot(parentDirectory, allowRoot: true))
        {
            return ScreenshotLibraryResult.Failure("Папка находится вне хранилища снимков.");
        }

        if (ValidateName(name) is { } nameError)
        {
            return ScreenshotLibraryResult.Failure(nameError);
        }

        var target = Path.Combine(Path.GetFullPath(parentDirectory), name);

        if (Directory.Exists(target) || File.Exists(target))
        {
            return ScreenshotLibraryResult.Failure($"«{name}» здесь уже есть.");
        }

        try
        {
            Directory.CreateDirectory(target);
            return ScreenshotLibraryResult.Success($"Создана папка «{name}».", target);
        }
        catch (Exception exception)
        {
            return ScreenshotLibraryResult.Failure($"Папка не создалась: {exception.Message}");
        }
    }

    public ScreenshotLibraryResult Rename(string fullPath, string newName)
    {
        if (IsRoot(fullPath))
        {
            return ScreenshotLibraryResult.Failure("Корневую папку хранилища переименовать нельзя.");
        }

        if (!IsInsideRoot(fullPath, allowRoot: false))
        {
            return ScreenshotLibraryResult.Failure("Переименовать можно только внутри хранилища снимков.");
        }

        var source = Path.GetFullPath(fullPath);
        var isFolder = Directory.Exists(source);

        if (!isFolder && !File.Exists(source))
        {
            return ScreenshotLibraryResult.Failure("Файла больше нет — обнови список.");
        }

        var targetName = isFolder ? newName.Trim() : ApplyExtension(source, newName.Trim());

        if (ValidateName(targetName) is { } nameError)
        {
            return ScreenshotLibraryResult.Failure(nameError);
        }

        var directory = Path.GetDirectoryName(source)!;
        var target = Path.Combine(directory, targetName);

        if (string.Equals(source, target, StringComparison.Ordinal))
        {
            return ScreenshotLibraryResult.Success("Имя не изменилось.", source);
        }

        // Смена только регистра — это тот же файл, и запрет по «уже существует» здесь неверен.
        var sameFileDifferentCase = string.Equals(source, target, StringComparison.OrdinalIgnoreCase);

        if (!sameFileDifferentCase && (File.Exists(target) || Directory.Exists(target)))
        {
            return ScreenshotLibraryResult.Failure($"«{targetName}» здесь уже есть.");
        }

        try
        {
            if (isFolder)
            {
                Directory.Move(source, target);
            }
            else
            {
                File.Move(source, target);
            }

            return ScreenshotLibraryResult.Success($"Теперь это «{targetName}».", target);
        }
        catch (Exception exception)
        {
            return ScreenshotLibraryResult.Failure($"Не переименовалось: {exception.Message}");
        }
    }

    public ScreenshotLibraryResult Move(IReadOnlyList<string> fullPaths, string targetDirectory)
    {
        if (!IsInsideRoot(targetDirectory, allowRoot: true))
        {
            return ScreenshotLibraryResult.Failure("Папка назначения находится вне хранилища снимков.");
        }

        var destination = Path.GetFullPath(targetDirectory);

        if (!Directory.Exists(destination))
        {
            return ScreenshotLibraryResult.Failure("Папки назначения больше нет.");
        }

        var moved = new List<string>();
        var problems = new List<string>();

        foreach (var path in fullPaths)
        {
            if (!IsInsideRoot(path, allowRoot: false))
            {
                problems.Add(IsRoot(path)
                    ? "корневую папку хранилища трогать нельзя"
                    : $"{Path.GetFileName(path)}: вне хранилища");
                continue;
            }

            var source = Path.GetFullPath(path);
            var isFolder = Directory.Exists(source);

            if (!isFolder && !File.Exists(source))
            {
                problems.Add($"{Path.GetFileName(source)}: файла больше нет");
                continue;
            }

            if (string.Equals(Path.GetDirectoryName(source), destination, StringComparison.OrdinalIgnoreCase))
            {
                // Уже на месте — это не ошибка, просто нечего делать.
                continue;
            }

            // Папку нельзя положить внутрь себя самой: получится потеря дерева.
            if (isFolder && IsSameOrInside(destination, source))
            {
                problems.Add($"{Path.GetFileName(source)}: нельзя переместить папку внутрь себя");
                continue;
            }

            var target = Path.Combine(destination, Path.GetFileName(source));

            if (File.Exists(target) || Directory.Exists(target))
            {
                problems.Add($"{Path.GetFileName(source)}: там уже есть такое имя");
                continue;
            }

            try
            {
                if (isFolder)
                {
                    Directory.Move(source, target);
                }
                else
                {
                    File.Move(source, target);
                }

                moved.Add(target);
            }
            catch (Exception exception)
            {
                problems.Add($"{Path.GetFileName(source)}: {exception.Message}");
            }
        }

        if (problems.Count == 0)
        {
            return ScreenshotLibraryResult.Success(
                moved.Count == 0 ? "Всё уже в этой папке." : $"Перемещено: {moved.Count}.",
                [.. moved]);
        }

        return new ScreenshotLibraryResult(
            moved.Count > 0,
            moved.Count > 0
                ? $"Перемещено: {moved.Count}. Не удалось: {string.Join("; ", problems)}"
                : $"Не удалось переместить: {string.Join("; ", problems)}",
            moved);
    }

    public ScreenshotLibraryResult Delete(IReadOnlyList<string> fullPaths)
    {
        var deleted = new List<string>();
        var problems = new List<string>();

        foreach (var path in fullPaths)
        {
            if (!IsInsideRoot(path, allowRoot: false))
            {
                problems.Add(IsRoot(path)
                    ? "корневую папку хранилища трогать нельзя"
                    : $"{Path.GetFileName(path)}: вне хранилища");
                continue;
            }

            var source = Path.GetFullPath(path);

            try
            {
                if (Directory.Exists(source))
                {
                    FileSystem.DeleteDirectory(
                        source,
                        UIOption.OnlyErrorDialogs,
                        RecycleOption.SendToRecycleBin,
                        UICancelOption.ThrowException);
                }
                else if (File.Exists(source))
                {
                    FileSystem.DeleteFile(
                        source,
                        UIOption.OnlyErrorDialogs,
                        RecycleOption.SendToRecycleBin,
                        UICancelOption.ThrowException);
                }
                else
                {
                    // Уже удалён снаружи — считаем, что цель достигнута.
                    continue;
                }

                deleted.Add(source);
            }
            catch (Exception exception)
            {
                problems.Add($"{Path.GetFileName(source)}: {exception.Message}");
            }
        }

        if (problems.Count == 0)
        {
            return ScreenshotLibraryResult.Success(
                deleted.Count == 0 ? "Удалять было нечего." : $"В корзину: {deleted.Count}.",
                [.. deleted]);
        }

        return new ScreenshotLibraryResult(
            deleted.Count > 0,
            deleted.Count > 0
                ? $"В корзину: {deleted.Count}. Не удалось: {string.Join("; ", problems)}"
                : $"Не удалось удалить: {string.Join("; ", problems)}",
            deleted);
    }

    /// <summary>
    /// Имя вводится без расширения, но если пользователь всё же его дописал — не удваиваем.
    /// </summary>
    private static string ApplyExtension(string sourcePath, string newName)
    {
        var extension = Path.GetExtension(sourcePath);

        if (extension.Length == 0)
        {
            return newName;
        }

        return newName.EndsWith(extension, StringComparison.OrdinalIgnoreCase)
            ? newName
            : newName + extension;
    }

    private bool IsRoot(string path)
    {
        try
        {
            return string.Equals(
                Path.TrimEndingDirectorySeparator(Path.GetFullPath(path)),
                Path.TrimEndingDirectorySeparator(Path.GetFullPath(RootDirectory)),
                StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception)
        {
            return false;
        }
    }

    private bool IsInsideRoot(string path, bool allowRoot)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return false;
        }

        string full;
        string root;

        try
        {
            full = Path.GetFullPath(path);
            root = Path.GetFullPath(RootDirectory);
        }
        catch (Exception)
        {
            // Путь с недопустимыми символами до диска и не дойдёт.
            return false;
        }

        if (string.Equals(full, root, StringComparison.OrdinalIgnoreCase))
        {
            return allowRoot;
        }

        return IsSameOrInside(full, root) && !string.Equals(full, root, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Лежит ли <paramref name="path"/> в <paramref name="container"/> или совпадает с ним.</summary>
    private static bool IsSameOrInside(string path, string container)
    {
        var full = Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
        var outer = Path.TrimEndingDirectorySeparator(Path.GetFullPath(container));

        if (string.Equals(full, outer, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        // Разделитель обязателен: иначе «screenshots-old» сойдёт за содержимое «screenshots».
        return full.StartsWith(outer + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    }

    private static string? ValidateName(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return "Имя не может быть пустым.";
        }

        if (name.Length > MaxNameLength)
        {
            return $"Слишком длинное имя: не больше {MaxNameLength} символов.";
        }

        if (name is "." or "..")
        {
            return "Такое имя недопустимо.";
        }

        var invalidChars = Path.GetInvalidFileNameChars();
        var used = name.Where(invalidChars.Contains).Distinct().ToArray();

        if (used.Length > 0)
        {
            var shown = used.Select(character => char.IsControl(character) ? "управляющий символ" : character.ToString());
            return $"В имени нельзя использовать: {string.Join(' ', shown)}";
        }

        if (name.EndsWith('.') || name.EndsWith(' '))
        {
            return "Имя не может заканчиваться точкой или пробелом.";
        }

        if (ReservedNames.Contains(Path.GetFileNameWithoutExtension(name)))
        {
            return $"«{name}» — зарезервированное имя Windows.";
        }

        return null;
    }
}
