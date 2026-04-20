using System.Collections.Concurrent;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using AdbControl.Application.Devices;

namespace AdbControl.Infrastructure.Devices;

public sealed class LocalNetworkAdbDiscoveryService : IDeviceDiscoveryService
{
    private const int AdbPort = 5555;
    private const int ProbeTimeoutMs = 350;
    private const int MaxParallelProbes = 64;

    public async Task<IReadOnlyList<DiscoveredAdbEndpoint>> DiscoverAsync(CancellationToken cancellationToken = default)
    {
        var scanTargets = GetScanTargets();
        var foundEndpoints = new ConcurrentBag<DiscoveredAdbEndpoint>();

        await Parallel.ForEachAsync(
            scanTargets,
            new ParallelOptions
            {
                CancellationToken = cancellationToken,
                MaxDegreeOfParallelism = MaxParallelProbes
            },
            async (target, token) =>
            {
                if (await IsPortOpenAsync(target.Address, AdbPort, token).ConfigureAwait(false))
                {
                    foundEndpoints.Add(new DiscoveredAdbEndpoint(
                        target.Address.ToString(),
                        $"{target.Address}:{AdbPort}",
                        target.NetworkLabel));
                }
            });

        return foundEndpoints
            .OrderBy(x => ToUInt32(IPAddress.Parse(x.Address)))
            .ToArray();
    }

    private static IReadOnlyList<ScanTarget> GetScanTargets()
    {
        var results = new List<ScanTarget>();
        var seenAddresses = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var networkInterface in NetworkInterface.GetAllNetworkInterfaces())
        {
            if (networkInterface.OperationalStatus != OperationalStatus.Up)
            {
                continue;
            }

            if (networkInterface.NetworkInterfaceType is NetworkInterfaceType.Loopback or NetworkInterfaceType.Tunnel)
            {
                continue;
            }

            var properties = networkInterface.GetIPProperties();
            if (!properties.GatewayAddresses.Any(x => x.Address.AddressFamily == AddressFamily.InterNetwork))
            {
                continue;
            }

            foreach (var unicast in properties.UnicastAddresses)
            {
                if (unicast.Address.AddressFamily != AddressFamily.InterNetwork)
                {
                    continue;
                }

                if (IPAddress.IsLoopback(unicast.Address))
                {
                    continue;
                }

                if (unicast.Address.ToString().StartsWith("169.254.", StringComparison.Ordinal))
                {
                    continue;
                }

                var prefixLength = Math.Clamp(unicast.PrefixLength, 24, 30);
                var networkAddress = ApplyMask(unicast.Address, prefixLength);
                var networkLabel = $"{networkAddress}/{prefixLength}";
                var localAddressValue = ToUInt32(unicast.Address);
                var firstHost = ToUInt32(networkAddress) + 1;
                var lastHostExclusive = ToUInt32(networkAddress) + HostCount(prefixLength) - 1;

                for (var current = firstHost; current < lastHostExclusive; current++)
                {
                    if (current == localAddressValue)
                    {
                        continue;
                    }

                    var address = FromUInt32(current);
                    if (seenAddresses.Add(address.ToString()))
                    {
                        results.Add(new ScanTarget(address, networkLabel));
                    }
                }
            }
        }

        return results;
    }

    private static async Task<bool> IsPortOpenAsync(IPAddress address, int port, CancellationToken cancellationToken)
    {
        using var tcpClient = new TcpClient();
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(TimeSpan.FromMilliseconds(ProbeTimeoutMs));

        try
        {
            await tcpClient.ConnectAsync(address, port, timeoutCts.Token).ConfigureAwait(false);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static IPAddress ApplyMask(IPAddress address, int prefixLength)
    {
        var mask = uint.MaxValue << (32 - prefixLength);
        return FromUInt32(ToUInt32(address) & mask);
    }

    private static uint HostCount(int prefixLength)
    {
        return 1u << (32 - prefixLength);
    }

    private static uint ToUInt32(IPAddress address)
    {
        var bytes = address.GetAddressBytes();
        if (BitConverter.IsLittleEndian)
        {
            Array.Reverse(bytes);
        }

        return BitConverter.ToUInt32(bytes, 0);
    }

    private static IPAddress FromUInt32(uint value)
    {
        var bytes = BitConverter.GetBytes(value);
        if (BitConverter.IsLittleEndian)
        {
            Array.Reverse(bytes);
        }

        return new IPAddress(bytes);
    }

    private sealed record ScanTarget(IPAddress Address, string NetworkLabel);
}
