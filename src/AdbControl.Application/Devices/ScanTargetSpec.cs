using System.Net;

namespace AdbControl.Application.Devices;

/// <summary>
/// Что именно сканировать. Закрытая иерархия: раскрытием занимается инфраструктура.
/// </summary>
public abstract record ScanTargetSpec
{
    public abstract string Label { get; }
}

/// <summary>Подсети всех активных сетевых интерфейсов машины.</summary>
public sealed record AutoLocalScanTarget : ScanTargetSpec
{
    public override string Label => "Локальные подсети";
}

/// <summary>Произвольная подсеть, например 212.59.101.0/24.</summary>
public sealed record CidrScanTarget(IPAddress Network, int PrefixLength) : ScanTargetSpec
{
    public override string Label => $"{Network}/{PrefixLength}";
}

/// <summary>Диапазон адресов, границы включительно.</summary>
public sealed record RangeScanTarget(IPAddress First, IPAddress Last) : ScanTargetSpec
{
    public override string Label => $"{First}-{Last}";
}

/// <summary>Один хост: IP-адрес или DNS-имя. Порты берутся из <see cref="PortSpec"/>.</summary>
public sealed record HostScanTarget(string Host) : ScanTargetSpec
{
    public override string Label => Host;
}

/// <summary>Хост с явным портом. <see cref="PortSpec"/> для такой цели не применяется.</summary>
public sealed record EndpointScanTarget(string Host, int Port) : ScanTargetSpec
{
    public override string Label => $"{Host}:{Port}";
}
