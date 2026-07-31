namespace AdbControl.Application.Devices;

public interface IMdnsDiscoveryService
{
    /// <summary>
    /// Службы ADB, видимые через mDNS. Работает только в пределах широковещательного домена:
    /// устройство за пробросом сюда не попадёт, его адрес нужно указывать явно.
    /// </summary>
    Task<IReadOnlyList<MdnsAdbService>> DiscoverAsync(CancellationToken cancellationToken = default);
}
