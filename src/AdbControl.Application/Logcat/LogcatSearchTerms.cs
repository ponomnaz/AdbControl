namespace AdbControl.Application.Logcat;

/// <summary>
/// Условия отбора строк лога. Хранятся списками, а не одной строкой с разделителем:
/// любой символ-разделитель рано или поздно встретится внутри искомой подстроки.
/// </summary>
/// <param name="Include">Строка проходит, если содержит хотя бы одно. Пустой список — проходят все.</param>
/// <param name="Exclude">Строка отбрасывается, если содержит хотя бы одно. Сильнее <paramref name="Include"/>.</param>
public sealed record LogcatSearchTerms(IReadOnlyList<string> Include, IReadOnlyList<string> Exclude)
{
    public static LogcatSearchTerms Empty { get; } = new([], []);

    public bool IsEmpty => Include.Count == 0 && Exclude.Count == 0;

    /// <summary>Сравнение без учёта регистра: в логах регистр тегов непостоянен.</summary>
    public bool Matches(string line)
    {
        foreach (var term in Exclude)
        {
            if (line.Contains(term, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }
        }

        if (Include.Count == 0)
        {
            return true;
        }

        foreach (var term in Include)
        {
            if (line.Contains(term, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }
}
