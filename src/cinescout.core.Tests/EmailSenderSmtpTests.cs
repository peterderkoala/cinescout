using cinescout.core.Email;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;

namespace cinescout.core.Tests;

/// <summary>
/// Exercises a real send against rnwood/smtp4dev (an ephemeral local SMTP test server), matching
/// this repo's preference for real infrastructure over mocking the transport it depends on. No
/// dedicated Testcontainers.* module exists for smtp4dev, so this uses the generic
/// <see cref="ContainerBuilder"/> directly, following smtp4dev's own documented Testcontainers example.
/// </summary>
public sealed class EmailSenderSmtpTests : IAsyncLifetime
{
    private const ushort WebPort = 80;
    private const ushort SmtpContainerPort = 25;

    private IContainer _smtp4dev = null!;
    private ushort _mappedWebPort;
    private ushort _mappedSmtpPort;

    public async Task InitializeAsync()
    {
        _smtp4dev = new ContainerBuilder("rnwood/smtp4dev:3.15.0")
            .WithPortBinding(WebPort, true)
            .WithPortBinding(SmtpContainerPort, true)
            .WithWaitStrategy(Wait.ForUnixContainer().UntilHttpRequestIsSucceeded(r => r.ForPort(WebPort)))
            .Build();

        await _smtp4dev.StartAsync();

        _mappedWebPort = _smtp4dev.GetMappedPublicPort(WebPort);
        _mappedSmtpPort = _smtp4dev.GetMappedPublicPort(SmtpContainerPort);
    }

    public async Task DisposeAsync() => await _smtp4dev.DisposeAsync();

    [Fact]
    public async Task Send_against_a_real_smtp_server_delivers_the_message()
    {
        var configuration = EmailSenderTests.ConfiguredWith(
            host: "localhost", port: _mappedSmtpPort, fromAddress: "cinescout@example.com");
        var sender = new EmailSender(configuration);

        var result = await sender.SendAsync(
            "recipient@example.com", "CineScout test subject", "CineScout test body", CancellationToken.None);

        Assert.True(result.Success);

        using var httpClient = new HttpClient { BaseAddress = new Uri($"http://localhost:{_mappedWebPort}") };
        var deliveredJson = await PollUntilContainsAsync(httpClient, "/api/messages", "CineScout test subject");

        Assert.Contains("CineScout test subject", deliveredJson);
        Assert.Contains("recipient@example.com", deliveredJson);
    }

    private static async Task<string> PollUntilContainsAsync(HttpClient httpClient, string path, string expected)
    {
        for (var attempt = 0; attempt < 20; attempt++)
        {
            var body = await httpClient.GetStringAsync(path);
            if (body.Contains(expected, StringComparison.Ordinal))
            {
                return body;
            }

            await Task.Delay(TimeSpan.FromMilliseconds(250));
        }

        return await httpClient.GetStringAsync(path);
    }
}
