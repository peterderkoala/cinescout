using System.Net.Sockets;
using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.Extensions.Configuration;
using MimeKit;
using Polly;
using Polly.Retry;
using AuthenticationException = MailKit.Security.AuthenticationException;

namespace cinescout.core.Email;

/// <summary>
/// Real implementation: SMTP host/port/credentials/from-address are read from configuration at
/// call time (not baked into a client at DI-registration time), mirroring
/// <see cref="cinescout.core.Discord.DiscordNotifier"/>'s posture. <c>AddStandardResilienceHandler</c>
/// doesn't apply here — it's HTTP-specific and SMTP isn't HTTP — so the retry pipeline is built
/// manually from <c>Microsoft.Extensions.Resilience</c>'s generic <see cref="ResiliencePipelineBuilder"/>.
/// </summary>
public sealed class EmailSender(IConfiguration configuration) : IEmailSender
{
    private static readonly ResiliencePipeline Pipeline = new ResiliencePipelineBuilder()
        .AddRetry(new RetryStrategyOptions
        {
            ShouldHandle = args => ValueTask.FromResult(IsKnownTransportFailure(args.Outcome.Exception)),
            BackoffType = DelayBackoffType.Exponential,
            MaxRetryAttempts = 3,
            Delay = TimeSpan.FromMilliseconds(200),
            UseJitter = true,
        })
        .Build();

    // The single source of truth for "known SMTP/transport failure" — shared by the retry
    // predicate above and the catch filter below, so the two can't silently drift apart.
    private static bool IsKnownTransportFailure(Exception? exception) =>
        exception is SmtpCommandException or SmtpProtocolException or AuthenticationException or SocketException or IOException;

    public async Task<EmailResult> SendAsync(string to, string subject, string body, CancellationToken cancellationToken)
    {
        var host = configuration["Email:SmtpHost"];
        var port = configuration.GetValue<int?>("Email:SmtpPort");
        var fromAddress = configuration["Email:FromAddress"];

        if (string.IsNullOrEmpty(host) || port is null || string.IsNullOrEmpty(fromAddress))
        {
            return new EmailResult(false, "Email not configured.");
        }

        var username = configuration["Email:Username"];
        var password = configuration["Email:Password"];

        var message = new MimeMessage();
        message.From.Add(MailboxAddress.Parse(fromAddress));
        message.To.Add(MailboxAddress.Parse(to));
        message.Subject = subject;
        message.Body = new TextPart("plain") { Text = body };

        try
        {
            await Pipeline.ExecuteAsync(async ct =>
            {
                using var client = new SmtpClient();
                await client.ConnectAsync(host, port.Value, SecureSocketOptions.Auto, ct);

                if (!string.IsNullOrEmpty(username))
                {
                    await client.AuthenticateAsync(username, password ?? string.Empty, ct);
                }

                await client.SendAsync(message, ct);
                await client.DisconnectAsync(true, ct);
            }, cancellationToken);

            return new EmailResult(true, "Sent.");
        }
        catch (Exception ex) when (IsKnownTransportFailure(ex))
        {
            return new EmailResult(false, ex.Message);
        }
    }
}
