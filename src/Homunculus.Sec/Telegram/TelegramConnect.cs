using System.Net;
using System.Net.Sockets;

namespace Homunculus.Sec.Telegram;

/// <summary>
/// Opens the TCP connection to the Bot API one address at a time, IPv4 first, each with its own short deadline. The default
/// behaviour is to try the first address the resolver returns (often IPv6) and wait for the operating system to give up on it,
/// which can take minutes when that route is silently dropped: the connection then sits in SYN-SENT and nothing else happens.
/// </summary>
public static class TelegramConnect
{
    public static readonly TimeSpan PerAddress = TimeSpan.FromSeconds(5);

    /// <summary>The <c>ConnectCallback</c> for a <see cref="SocketsHttpHandler"/>. TLS is layered on the returned stream by the handler.</summary>
    public static async ValueTask<Stream> ConnectAsync(SocketsHttpConnectionContext context, CancellationToken cancellation)
    {
        var host = context.DnsEndPoint.Host;
        var addresses = IPAddress.TryParse(host, out var literal)
            ? [literal]
            : Order(await Dns.GetHostAddressesAsync(host, cancellation));

        return await ConnectAsync(addresses, context.DnsEndPoint.Port, PerAddress, cancellation);
    }

    /// <summary>IPv4 addresses before IPv6 ones, otherwise in the order the resolver gave.</summary>
    public static IReadOnlyList<IPAddress> Order(IEnumerable<IPAddress> addresses) =>
        addresses.OrderBy(a => a.AddressFamily == AddressFamily.InterNetwork ? 0 : 1).ToList();

    /// <summary>Tries each address in the order given, each for at most <paramref name="perAddress"/>; returns the first stream that connects.</summary>
    /// <exception cref="SocketException">None of the addresses could be reached.</exception>
    public static async Task<Stream> ConnectAsync(IEnumerable<IPAddress> addresses, int port, TimeSpan perAddress, CancellationToken cancellation)
    {
        Exception? last = null;
        foreach (var address in addresses)
        {
            var socket = new Socket(address.AddressFamily, SocketType.Stream, ProtocolType.Tcp) { NoDelay = true };
            try
            {
                using var limit = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
                limit.CancelAfter(perAddress);
                await socket.ConnectAsync(new IPEndPoint(address, port), limit.Token);
                return new NetworkStream(socket, ownsSocket: true);
            }
            catch (Exception e) when (e is SocketException or OperationCanceledException && !cancellation.IsCancellationRequested)
            {
                socket.Dispose();
                last = e;
            }
            catch
            {
                socket.Dispose();
                throw;
            }
        }

        throw new SocketException((int)SocketError.HostUnreachable, last?.Message);
    }
}
