using System.Net;
using System.Net.Sockets;
using cinescout.core.Email;

namespace cinescout.core.Tests;

/// <summary>
/// Proves sends actually go through a retry pipeline rather than failing on the first transient
/// error: a raw <see cref="TcpListener"/> that accepts and immediately closes each connection
/// simulates a transient SMTP failure MailKit surfaces as an IOException, which is in
/// <see cref="EmailSender"/>'s known-exception retry set.
/// </summary>
public sealed class EmailSenderResilienceTests
{
    [Fact]
    public async Task Send_on_transient_failure_retries_multiple_times_before_giving_up()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;

        var acceptCount = 0;
        using var cts = new CancellationTokenSource();
        var acceptLoop = Task.Run(async () =>
        {
            while (!cts.IsCancellationRequested)
            {
                try
                {
                    using var client = await listener.AcceptTcpClientAsync(cts.Token);
                    Interlocked.Increment(ref acceptCount);
                    client.Close();
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (ObjectDisposedException)
                {
                    break;
                }
            }
        });

        var configuration = EmailSenderTests.ConfiguredWith(host: "localhost", port: port, fromAddress: "cinescout@example.com");
        var sender = new EmailSender(configuration);

        var result = await sender.SendAsync("recipient@example.com", "Subject", "Body", CancellationToken.None);

        cts.Cancel();
        listener.Stop();
        await acceptLoop;

        Assert.False(result.Success);
        Assert.True(acceptCount > 1, $"Expected more than one connection attempt (retries), got {acceptCount}.");
    }
}
