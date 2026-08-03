namespace AdbControl.Application.Devices;

/// <summary>
/// Итог очистки кэша. Отдельно от <see cref="DeviceActionBatchResult"/>, потому что
/// <c>rm -rf</c> возвращает ноль даже когда удалять было нечего: успех приходится
/// подтверждать проверкой, а объём — единственное, что показывает реальный эффект.
/// </summary>
/// <param name="RelaunchedCount">
/// Сколько приложений удалось поднять обратно. Отдельно от <paramref name="SuccessCount"/>:
/// кэш может быть очищен успешно, а запуск не удаться — это разные исходы.
/// </param>
public sealed record CacheClearBatchResult(
    int RequestedCount,
    int SuccessCount,
    int FailureCount,
    long FreedBytes,
    int RelaunchedCount);
