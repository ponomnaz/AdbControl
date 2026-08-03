using System.Text;

namespace AdbControl.Application.Logcat;

/// <summary>Порог важности: logcat показывает выбранный уровень и всё, что выше него.</summary>
public enum LogcatLevel
{
    All,
    Debug,
    Info,
    Warn,
    Error
}

/// <summary>
/// Фильтр, применяемый на устройстве. В отличие от <see cref="LogcatSearchTerms"/>
/// отсечённые строки не придут никогда — вернуть их можно только перезапуском.
/// </summary>
public sealed record LogcatFilterSettings(
    LogcatLevel Level,
    IReadOnlyList<string> Tags,
    bool OnlyNetarium,
    string ExtraArguments)
{
    public static LogcatFilterSettings Default { get; } = new(LogcatLevel.All, [], false, string.Empty);

    /// <summary>
    /// Собирает аргументы logcat. Порядок важен: «глушитель» <c>*:S</c> идёт после тегов,
    /// а произвольные аргументы — последними, чтобы ими можно было перебить остальное.
    /// </summary>
    public string BuildArguments(int? netariumProcessId)
    {
        var builder = new StringBuilder();

        if (OnlyNetarium && netariumProcessId is { } processId)
        {
            builder.Append("--pid=").Append(processId);
        }

        var priority = ToPriority(Level);
        var usableTags = Tags.Where(IsValidTag).ToArray();

        if (usableTags.Length > 0)
        {
            foreach (var tag in usableTags)
            {
                Append(builder, $"{tag}:{priority}");
            }

            // Без этого остальные теги продолжат сыпаться в лог.
            Append(builder, "*:S");
        }
        else if (Level != LogcatLevel.All)
        {
            Append(builder, $"*:{priority}");
        }

        if (!string.IsNullOrWhiteSpace(ExtraArguments))
        {
            Append(builder, ExtraArguments.Trim());
        }

        return builder.ToString();
    }

    /// <summary>
    /// В выражении фильтра тег отделяется от уровня двоеточием, поэтому теги с двоеточием
    /// или пробелами logcat отвергает целиком: «Invalid filter expression». Такие строки
    /// отсекаются только на стороне приложения, через условия скрытия.
    /// </summary>
    public static bool IsValidTag(string tag)
    {
        return !string.IsNullOrWhiteSpace(tag) &&
               !tag.Any(character => character == ':' || char.IsWhiteSpace(character));
    }

    private static void Append(StringBuilder builder, string value)
    {
        if (builder.Length > 0)
        {
            builder.Append(' ');
        }

        builder.Append(value);
    }

    private static char ToPriority(LogcatLevel level)
    {
        return level switch
        {
            LogcatLevel.Debug => 'D',
            LogcatLevel.Info => 'I',
            LogcatLevel.Warn => 'W',
            LogcatLevel.Error => 'E',
            _ => 'V'
        };
    }
}
