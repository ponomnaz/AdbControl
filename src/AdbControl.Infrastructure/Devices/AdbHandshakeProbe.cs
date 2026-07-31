using System.Buffers.Binary;
using System.Text;
using AdbControl.Application.Devices;

namespace AdbControl.Infrastructure.Devices;

internal sealed record AdbHandshakeResult(AdbEndpointState State, string? Model)
{
    public static AdbHandshakeResult NotAdb { get; } = new(AdbEndpointState.PortOpen, null);
}

/// <summary>
/// Одно рукопожатие протокола ADB: отправляем A_CNXN и разбираем ответный заголовок.
/// Реализовывать протокол целиком не нужно — достаточно 24-байтной шапки и magic.
///
/// Важно: на A_AUTH мы намеренно не отвечаем. Диалог «Разрешить отладку по USB?»
/// на устройстве появляется только после отправки хостом открытого ключа, поэтому
/// сканирование остаётся незаметным для пользователя устройства.
/// </summary>
internal static class AdbHandshakeProbe
{
    private const uint ConnectCommand = 0x4E584E43; // "CNXN"
    private const uint AuthCommand = 0x48545541;    // "AUTH"
    private const uint TlsCommand = 0x534C5453;     // "STLS"
    private const uint ProtocolVersion = 0x01000001;
    private const uint MaxPayload = 1024 * 1024;
    private const int HeaderLength = 24;
    private const int MaxBannerLength = 8192;

    private static readonly byte[] ConnectPayload = Encoding.ASCII.GetBytes("host::features=cmd,shell_v2\0");

    public static async Task<AdbHandshakeResult> TryHandshakeAsync(
        Stream stream,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(timeout);

        try
        {
            await stream.WriteAsync(BuildConnectMessage(), timeoutCts.Token).ConfigureAwait(false);
            await stream.FlushAsync(timeoutCts.Token).ConfigureAwait(false);

            var header = new byte[HeaderLength];
            await stream.ReadExactlyAsync(header, timeoutCts.Token).ConfigureAwait(false);

            var command = BinaryPrimitives.ReadUInt32LittleEndian(header);
            var magic = BinaryPrimitives.ReadUInt32LittleEndian(header.AsSpan(20));

            if ((command ^ 0xFFFFFFFFu) != magic)
            {
                return AdbHandshakeResult.NotAdb;
            }

            switch (command)
            {
                case AuthCommand:
                    return new AdbHandshakeResult(AdbEndpointState.AdbUnauthorized, null);

                case TlsCommand:
                    return new AdbHandshakeResult(AdbEndpointState.AdbTlsRequired, null);

                case ConnectCommand:
                    break;

                default:
                    return AdbHandshakeResult.NotAdb;
            }

            var payloadLength = BinaryPrimitives.ReadUInt32LittleEndian(header.AsSpan(12));
            if (payloadLength is 0 or > MaxBannerLength)
            {
                return new AdbHandshakeResult(AdbEndpointState.AdbReady, null);
            }

            var payload = new byte[payloadLength];
            await stream.ReadExactlyAsync(payload, timeoutCts.Token).ConfigureAwait(false);

            return new AdbHandshakeResult(
                AdbEndpointState.AdbReady,
                ParseModel(Encoding.UTF8.GetString(payload).TrimEnd('\0')));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            // Молчание, обрыв или мусор в ответ — значит, за портом не ADB.
            return AdbHandshakeResult.NotAdb;
        }
    }

    private static byte[] BuildConnectMessage()
    {
        var message = new byte[HeaderLength + ConnectPayload.Length];
        var span = message.AsSpan();

        BinaryPrimitives.WriteUInt32LittleEndian(span, ConnectCommand);
        BinaryPrimitives.WriteUInt32LittleEndian(span[4..], ProtocolVersion);
        BinaryPrimitives.WriteUInt32LittleEndian(span[8..], MaxPayload);
        BinaryPrimitives.WriteUInt32LittleEndian(span[12..], (uint)ConnectPayload.Length);
        BinaryPrimitives.WriteUInt32LittleEndian(span[16..], Checksum(ConnectPayload));
        BinaryPrimitives.WriteUInt32LittleEndian(span[20..], ConnectCommand ^ 0xFFFFFFFFu);
        ConnectPayload.CopyTo(span[HeaderLength..]);

        return message;
    }

    /// <summary>Контрольная сумма ADB — обычная сумма байтов, а не CRC32, вопреки имени поля.</summary>
    private static uint Checksum(ReadOnlySpan<byte> payload)
    {
        var sum = 0u;
        foreach (var value in payload)
        {
            sum += value;
        }

        return sum;
    }

    /// <summary>
    /// Баннер устройства выглядит как
    /// <c>device::ro.product.name=x;ro.product.model=Y;ro.product.device=z;features=...</c>
    /// </summary>
    private static string? ParseModel(string banner)
    {
        const string key = "ro.product.model=";

        var keyIndex = banner.IndexOf(key, StringComparison.Ordinal);
        if (keyIndex < 0)
        {
            return null;
        }

        var start = keyIndex + key.Length;
        var end = banner.IndexOf(';', start);
        var model = (end < 0 ? banner[start..] : banner[start..end]).Trim();

        return string.IsNullOrWhiteSpace(model) ? null : model;
    }
}
