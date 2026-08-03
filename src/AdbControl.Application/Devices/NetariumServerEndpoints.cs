namespace AdbControl.Application.Devices;

/// <summary>Текущий адрес сервера и сохранённый список для быстрого выбора.</summary>
/// <param name="Endpoints">
/// <c>null</c> означает, что списка в настройках ещё не было — тогда каталог заводит
/// адрес по умолчанию. Пустой список — это осознанно очищенный пользователем список,
/// и наполнять его заново нельзя, иначе удалённое возвращается само.
/// </param>
public sealed record NetariumServerEndpoints(string Selected, IReadOnlyList<string>? Endpoints);
