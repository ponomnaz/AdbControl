using System.Net;
using System.Net.Sockets;

namespace AdbControl.Application.Devices;

public static class IPv4Math
{
    public static bool IsIPv4(IPAddress address)
    {
        return address.AddressFamily == AddressFamily.InterNetwork;
    }

    public static uint ToUInt32(IPAddress address)
    {
        var bytes = address.GetAddressBytes();
        if (BitConverter.IsLittleEndian)
        {
            Array.Reverse(bytes);
        }

        return BitConverter.ToUInt32(bytes, 0);
    }

    public static IPAddress FromUInt32(uint value)
    {
        var bytes = BitConverter.GetBytes(value);
        if (BitConverter.IsLittleEndian)
        {
            Array.Reverse(bytes);
        }

        return new IPAddress(bytes);
    }

    public static uint Mask(int prefixLength)
    {
        return prefixLength <= 0
            ? 0u
            : uint.MaxValue << (32 - prefixLength);
    }

    public static IPAddress NetworkAddress(IPAddress address, int prefixLength)
    {
        return FromUInt32(ToUInt32(address) & Mask(prefixLength));
    }

    public static long BlockSize(int prefixLength)
    {
        return 1L << (32 - prefixLength);
    }

    public static bool IsInNetwork(uint address, uint network, int prefixLength)
    {
        var mask = Mask(prefixLength);
        return (address & mask) == (network & mask);
    }

    /// <summary>
    /// Первый и последний адрес блока, пригодные для проб. Для префиксов /30 и короче
    /// адрес сети и широковещательный адрес исключаются, для /31 и /32 берётся весь блок.
    /// </summary>
    public static (uint First, uint Last) HostRange(uint network, int prefixLength)
    {
        var blockSize = (uint)BlockSize(prefixLength);
        if (prefixLength >= 31)
        {
            return (network, network + blockSize - 1);
        }

        return (network + 1, network + blockSize - 2);
    }

    /// <summary>
    /// Самый короткий префикс, чей блок укладывается в <paramref name="maxHosts"/> адресов.
    /// </summary>
    public static int SmallestPrefixWithin(int maxHosts)
    {
        for (var prefixLength = 32; prefixLength >= 0; prefixLength--)
        {
            if (BlockSize(prefixLength) > maxHosts)
            {
                return prefixLength + 1;
            }
        }

        return 0;
    }
}
