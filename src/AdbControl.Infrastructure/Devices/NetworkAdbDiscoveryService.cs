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

            var localSettings = new GroupSettings(
                tuning.LocalConnectTimeout,
                tuning.LocalHandshakeTimeout,
                tuning.VerifyAdbHandshake);

            var remoteSettings = new GroupSettings(
                tuning.RemoteConnectTimeout,
                tuning.RemoteHandshakeTimeout,
                tuning.VerifyAdbHandshake);

            // Удалённые цели идут отдельным пулом: длиннее таймаут, ниже параллельность,
            // иначе проброс за NAT либо не успевает ответить, либо захлёбывается.
            await Task.WhenAll(
                    ProbeGroupAsync(localProbes, localSettings, tuning.LocalParallelism, writer, counters, cancellationToken),
                    ProbeGroupAsync(remoteProbes, remoteSettings, tuning.RemoteParallelism, writer, counters, cancellationToken))
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
        GroupSettings settings,
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
                var outcome = await TryProbeAsync(probe, settings, token).ConfigureAwait(false);

                if (outcome is { } found)
                {
                    writer.TryWrite(new DiscoveredAdbEndpoint(
                        probe.Address.ToString(),
                        probe.Port,
                        $"{probe.Address}:{probe.Port}",
                        probe.NetworkLabel,
                        found.ResponseTime,
                        found.State,
                        found.Model));

                    counters.OnFound();
                }

                counters.OnCompleted();
            }).ConfigureAwait(false);
    }

    private static async Task<ProbeOutcome?> TryProbeAsync(
        ScanProbeTarget probe,
        GroupSettings settings,
        CancellationToken cancellationToken)
    {
        using var tcpClient = new TcpClient(probe.Address.AddressFamily);
        var stopwatch = Stopwatch.StartNew();

        try
        {
            using (var connectCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
            {
                connectCts.CancelAfter(settings.ConnectTimeout);
                await tcpClient.ConnectAsync(probe.Address, probe.Port, connectCts.Token).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            return null;
        }

        var responseTime = stopwatch.Elapsed;

        if (!settings.VerifyAdbHandshake)
        {
            return new ProbeOutcome(responseTime, AdbEndpointState.Unknown, null);
        }

        // Сокет уже открыт — рукопожатие идёт по нему же, второго подключения не требуется.
        var handshake = await AdbHandshakeProbe
            .TryHandshakeAsync(tcpClient.GetStream(), settings.HandshakeTimeout, cancellationToken)
            .ConfigureAwait(false);

        return new ProbeOutcome(responseTime, handshake.State, handshake.Model);
    }

    private readonly record struct ProbeOutcome(TimeSpan ResponseTime, AdbEndpointState State, string? Model);

    private readonly record struct GroupSettings(
        TimeSpan ConnectTimeout,
        TimeSpan HandshakeTimeout,
        bool VerifyAdbHandshake);

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
