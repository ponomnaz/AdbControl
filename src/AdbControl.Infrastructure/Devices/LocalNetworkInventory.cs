using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using AdbControl.Application.Devices;

namespace AdbControl.Infrastructure.Devices;

internal sealed record LocalNetwork(IPAddress Address, uint AddressValue, uint NetworkValue, int PrefixLength);

internal static class LocalNetworkInventory
{
    // Префиксы короче /8 в юникаст-адресах не встречаются и ломают определение локальности.
    private const int MinPrefixLength = 8;

    /// <summary>
    /// Подсети активных интерфейсов. В отличие от прежней логики, туннели (VPN) и интерфейсы
    /// без шлюза не отбрасываются: устройство может быть достижимо именно через них.
    /// </summary>
    public static IReadOnlyList<LocalNetwork> GetLocalNetworks()
    {
        var results = new List<LocalNetwork>();

        foreach (var networkInterface in NetworkInterface.GetAllNetworkInterfaces())
        {
            if (networkInterface.OperationalStatus != OperationalStatus.Up ||
                networkInterface.NetworkInterfaceType == NetworkInterfaceType.Loopback)
            {
                continue;
            }

            foreach (var unicast in networkInterface.GetIPProperties().UnicastAddresses)
            {
                if (unicast.Address.AddressFamily != AddressFamily.InterNetwork ||
                    IPAddress.IsLoopback(unicast.Address) ||
                    unicast.Address.ToString().StartsWith("169.254.", StringComparison.Ordinal))
                {
                    continue;
                }

                var prefixLength = unicast.PrefixLength;
                if (prefixLength is < MinPrefixLength or > 32)
                {
                    continue;
                }

                var addressValue = IPv4Math.ToUInt32(unicast.Address);
                results.Add(new LocalNetwork(
                    unicast.Address,
                    addressValue,
                    addressValue & IPv4Math.Mask(prefixLength),
                    prefixLength));
            }
        }

        return results;
    }

    public static bool IsLocal(IReadOnlyList<LocalNetwork> networks, uint address)
    {
        foreach (var network in networks)
        {
            if (IPv4Math.IsInNetwork(address, network.NetworkValue, network.PrefixLength))
            {
                return true;
            }
        }

        return false;
    }
}
