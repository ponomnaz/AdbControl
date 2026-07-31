using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Runtime.CompilerServices;
using System.Threading.Channels;
using AdbControl.Application.Devices;

namespace AdbControl.Infrastructure.Devices;

public sealed class NetworkAdbDiscoveryService : IDeviceDiscoveryService
{
    private const int ProgressReportInterval = 16;

    private readonly ScanTargetExpander _expander = new();

    public async IAsyncEnumerable<DiscoveredAdbEndpoint> DiscoverAsync(
        ScanProfile profile,
        IProgress<ScanProgress>? progress = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var probes = await _expander.ExpandAsync(profile, cancellationToken).ConfigureAwait(false);
        if (probes.Count == 0)
        {
            progress?.Report(new ScanProgress(0, 0, 0));
            yield break;
        }

        var channel = Channel.CreateUnbounded<DiscoveredAdbEndpoint>(new UnboundedChannelOptions
        {
            SingleReader = true
        });

        var counters = new ProbeCounters(probes.Count, progress);

        // Отдельный источник отмены: если потребитель прекратит перечисление, пробы должны остановиться.
        using var producerCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var producer = RunProbesAsync(probes, profile.Tuning, channel.Writer, counters, producerCts.Token);

        try
        {
            await foreach (var endpoint in channel.Reader.ReadAllAsync(cancellationToken).ConfigureAwait(false))
            {
                yield return endpoint;
            }
        }
        finally
        {
            await producerCts.CancelAsync().ConfigureAwait(false);
            await producer.ConfigureAwait(false);
        }
    }

    private static async Task RunProbesAsync(
        IReadOnlyList<ScanProbeTarget> probes,
        ScanTuning tuning,
        ChannelWriter<DiscoveredAdbEndpoint> writer,
        ProbeCounters counters,
        CancellationToken cancellationToken)
    {
        try
        {
            var localProbes = probes.Where(static probe => probe.IsLocal).ToArray();
            var remoteProbes = probes.Where(static probe => !probe.IsLocal).ToArray();

            // Удалённые цели идут отдельным пулом: длиннее таймаут, ниже параллельность,
            // иначе проброс за NAT либо не успевает ответить, либо захлёбывается.
            await Task.WhenAll(
                    ProbeGroupAsync(localProbes, tuning.LocalConnectTimeout, tuning.LocalParallelism, writer, counters, cancellationToken),
                    ProbeGroupAsync(remoteProbes, tuning.RemoteConnectTimeout, tuning.RemoteParallelism, writer, counters, cancellationToken))
                .ConfigureAwait(false);

            counters.ReportFinal();
            writer.TryComplete();
        }
        catch (OperationCanceledException)
        {
            writer.TryComplete();
        }
        catch (Exception exception)
        {
            writer.TryComplete(exception);
        }
    }

    private static async Task ProbeGroupAsync(
        IReadOnlyList<ScanProbeTarget> probes,
        TimeSpan timeout,
        int parallelism,
        ChannelWriter<DiscoveredAdbEndpoint> writer,
        ProbeCounters counters,
        CancellationToken cancellationToken)
    {
        if (probes.Count == 0)
        {
            return;
        }

        await Parallel.ForEachAsync(
            probes,
            new ParallelOptions
            {
                CancellationToken = cancellationToken,
                MaxDegreeOfParallelism = Math.Max(1, parallelism)
            },
            async (probe, token) =>
            {
                var responseTime = await TryConnectAsync(probe.Address, probe.Port, timeout, token).ConfigureAwait(false);

                if (responseTime is { } elapsed)
                {
                    writer.TryWrite(new DiscoveredAdbEndpoint(
                        probe.Address.ToString(),
                        probe.Port,
                        $"{probe.Address}:{probe.Port}",
                        probe.NetworkLabel,
                        elapsed));

                    counters.OnFound();
                }

                counters.OnCompleted();
            }).ConfigureAwait(false);
    }

    private static async Task<TimeSpan?> TryConnectAsync(
        IPAddress address,
        int port,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        using var tcpClient = new TcpClient(address.AddressFamily);
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(timeout);

        var stopwatch = Stopwatch.StartNew();

        try
        {
            await tcpClient.ConnectAsync(address, port, timeoutCts.Token).ConfigureAwait(false);
            return stopwatch.Elapsed;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            return null;
        }
    }

    private sealed class ProbeCounters
    {
        private readonly IProgress<ScanProgress>? _progress;
        private int _completed;
        private int _found;

        public ProbeCounters(int totalProbes, IProgress<ScanProgress>? progress)
        {
            TotalProbes = totalProbes;
            _progress = progress;
        }

        public int TotalProbes { get; }

        public void OnFound()
        {
            Interlocked.Increment(ref _found);
        }

        public void OnCompleted()
        {
            var completed = Interlocked.Increment(ref _completed);
            if (completed % ProgressReportInterval == 0)
            {
                _progress?.Report(new ScanProgress(completed, TotalProbes, Volatile.Read(ref _found)));
            }
        }

        public void ReportFinal()
        {
            _progress?.Report(new ScanProgress(Volatile.Read(ref _completed), TotalProbes, Volatile.Read(ref _found)));
        }
    }
}
