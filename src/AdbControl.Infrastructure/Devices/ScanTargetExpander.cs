using System.Net;
using System.Net.Sockets;
using AdbControl.Application.Devices;

namespace AdbControl.Infrastructure.Devices;

internal sealed record ScanProbeTarget(IPAddress Address, int Port, string NetworkLabel, bool IsLocal);

/// <summary>
/// Превращает цели профиля в плоский список проб «адрес + порт».
/// Размер области считается до материализации, иначе широкая маска съедает память.
/// </summary>
internal sealed class ScanTargetExpander
{
    public async Task<IReadOnlyList<ScanProbeTarget>> ExpandAsync(
        ScanProfile profile,
        CancellationToken cancellationToken)
    {
        var ports = profile.Ports.Enumerate().Distinct().ToArray();
        if (ports.Length == 0 || profile.Targets.Count == 0)
        {
            return [];
        }

        var localNetworks = LocalNetworkInventory.GetLocalNetworks();
        var blocks = new List<AddressBlock>();

        foreach (var target in profile.Targets)
        {
            blocks.AddRange(await ResolveAsync(target, localNetworks, profile.Tuning, cancellationToken).ConfigureAwait(false));
        }

        blocks.RemoveAll(static block => block.First > block.Last);

        var totalProbes = blocks.Sum(block => block.Count * (block.FixedPort.HasValue ? 1 : ports.Length));
        if (totalProbes > profile.Tuning.MaxProbeBudget)
        {
            throw new ScanBudgetExceededException(totalProbes, profile.Tuning.MaxProbeBudget);
        }

        var localAddresses = localNetworks.Select(static network => network.AddressValue).ToHashSet();
        var seen = new HashSet<(uint Address, int Port)>();
        var probes = new List<ScanProbeTarget>((int)totalProbes);

        foreach (var block in blocks)
        {
            int[] blockPorts = block.FixedPort is { } fixedPort ? [fixedPort] : ports;

            for (var value = block.First; ; value++)
            {
                cancellationToken.ThrowIfCancellationRequested();

                // Собственные адреса машины не пробуем: adb на них не слушает.
                if (!localAddresses.Contains(value))
                {
                    var address = IPv4Math.FromUInt32(value);
                    var isLocal = LocalNetworkInventory.IsLocal(localNetworks, value);

                    foreach (var port in blockPorts)
                    {
                        if (seen.Add((value, port)))
                        {
                            probes.Add(new ScanProbeTarget(address, port, block.NetworkLabel, isLocal));
                        }
                    }
                }

                // Сравнение в конце тела, чтобы не переполнить uint на 255.255.255.255.
                if (value == block.Last)
                {
                    break;
                }
            }
        }

        return probes;
    }

    private static async Task<IReadOnlyList<AddressBlock>> ResolveAsync(
        ScanTargetSpec target,
        IReadOnlyList<LocalNetwork> localNetworks,
        ScanTuning tuning,
        CancellationToken cancellationToken)
    {
        switch (target)
        {
            case AutoLocalScanTarget:
                return ExpandAutoLocal(localNetworks, tuning);

            case CidrScanTarget cidr:
            {
                var (first, last) = IPv4Math.HostRange(IPv4Math.ToUInt32(cidr.Network), cidr.PrefixLength);
                return [new AddressBlock(first, last, cidr.Label, null)];
            }

            case RangeScanTarget range:
                return
                [
                    new AddressBlock(
                        IPv4Math.ToUInt32(range.First),
                        IPv4Math.ToUInt32(range.Last),
                        range.Label,
                        null)
                ];

            case HostScanTarget host:
                return await ResolveHostBlocksAsync(host.Host, host.Label, null, cancellationToken).ConfigureAwait(false);

            case EndpointScanTarget endpoint:
                return await ResolveHostBlocksAsync(endpoint.Host, endpoint.Label, endpoint.Port, cancellationToken).ConfigureAwait(false);

            default:
                return [];
        }
    }

    /// <summary>
    /// Автосканирование локальных подсетей. Реальный префикс больше не приводится жёстко к /24,
    /// но и /16 целиком не перебирается — область ограничена <see cref="ScanTuning.MaxAutoLocalHosts"/>.
    /// </summary>
    private static IReadOnlyList<AddressBlock> ExpandAutoLocal(
        IReadOnlyList<LocalNetwork> localNetworks,
        ScanTuning tuning)
    {
        var minPrefixLength = IPv4Math.SmallestPrefixWithin(tuning.MaxAutoLocalHosts);
        var blocks = new List<AddressBlock>();

        foreach (var localNetwork in localNetworks)
        {
            var prefixLength = Math.Max(localNetwork.PrefixLength, minPrefixLength);
            var network = localNetwork.AddressValue & IPv4Math.Mask(prefixLength);
            var (first, last) = IPv4Math.HostRange(network, prefixLength);
            blocks.Add(new AddressBlock(first, last, $"{IPv4Math.FromUInt32(network)}/{prefixLength}", null));
        }

        return blocks;
    }

    private static async Task<IReadOnlyList<AddressBlock>> ResolveHostBlocksAsync(
        string host,
        string label,
        int? fixedPort,
        CancellationToken cancellationToken)
    {
        var addresses = await ResolveHostAsync(host, cancellationToken).ConfigureAwait(false);

        return addresses
            .Select(address =>
            {
                var value = IPv4Math.ToUInt32(address);
                return new AddressBlock(value, value, label, fixedPort);
            })
            .ToArray();
    }

    private static async Task<IReadOnlyList<IPAddress>> ResolveHostAsync(string host, CancellationToken cancellationToken)
    {
        if (IPAddress.TryParse(host, out var parsed))
        {
            return IPv4Math.IsIPv4(parsed) ? [parsed] : [];
        }

        try
        {
            return await Dns.GetHostAddressesAsync(host, AddressFamily.InterNetwork, cancellationToken).ConfigureAwait(false);
        }
        catch (SocketException)
        {
            return [];
        }
    }

    private readonly record struct AddressBlock(uint First, uint Last, string NetworkLabel, int? FixedPort)
    {
        public long Count => (long)Last - First + 1;
    }
}
