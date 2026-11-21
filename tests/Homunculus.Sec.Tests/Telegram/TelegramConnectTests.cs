using System.Net;
using System.Net.Sockets;
using Homunculus.Sec.Telegram;

namespace Homunculus.Sec.Tests.Telegram;

public class TelegramConnectTests
{
    [Test]
    public void Ipv4AddressesComeBeforeIpv6ButOtherwiseKeepTheirOrder()
    {
        IPAddress[] resolved = [IPAddress.Parse("2001:db8::1"), IPAddress.Parse("149.154.166.110"), IPAddress.Parse("2001:db8::2"), IPAddress.Parse("149.154.167.220")];

        var ordered = TelegramConnect.Order(resolved).Select(a => a.ToString());

        Assert.That(ordered, Is.EqualTo(new[] { "149.154.166.110", "149.154.167.220", "2001:db8::1", "2001:db8::2" }));
    }

    [Test]
    public async Task FallsBackToTheNextAddressWhenTheFirstOneRefuses()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;

        // Nothing listens on IPv6 loopback at this port, so that attempt fails at once and the IPv4 one must be used.
        await using var stream = await TelegramConnect.ConnectAsync([IPAddress.IPv6Loopback, IPAddress.Loopback], port, TimeSpan.FromSeconds(2), CancellationToken.None);

        using var accepted = await listener.AcceptTcpClientAsync();
        Assert.That(accepted.Connected, Is.True);
    }

    [Test]
    public void GivesUpWithASocketExceptionWhenNoAddressWorks()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();

        Assert.ThrowsAsync<SocketException>(() => TelegramConnect.ConnectAsync([IPAddress.Loopback], port, TimeSpan.FromSeconds(2), CancellationToken.None));
    }

    [Test]
    public void ACancelledCallIsNotRetriedOnTheNextAddress()
    {
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();

        Assert.CatchAsync<OperationCanceledException>(() => TelegramConnect.ConnectAsync([IPAddress.Loopback, IPAddress.IPv6Loopback], 9, TimeSpan.FromSeconds(2), cancelled.Token));
    }
}
