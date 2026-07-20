using System.Globalization;
using cinescout.core.Email;
using Microsoft.Extensions.Configuration;

namespace cinescout.core.Tests;

public class EmailSenderTests
{
    [Fact]
    public async Task Send_with_no_smtp_config_returns_not_configured_without_attempting_a_connection()
    {
        var configuration = new ConfigurationBuilder().Build();
        var sender = new EmailSender(configuration);

        var result = await sender.SendAsync("someone@example.com", "Subject", "Body", CancellationToken.None);

        Assert.False(result.Success);
        Assert.Equal("Email not configured.", result.Detail);
    }

    [Fact]
    public async Task Send_with_a_null_recipient_propagates_the_exception_instead_of_swallowing_it()
    {
        var configuration = ConfiguredWith(host: "localhost", port: 25, fromAddress: "cinescout@example.com");
        var sender = new EmailSender(configuration);

        // ArgumentNullException (from MimeKit's address parsing) isn't in EmailSender's known
        // SMTP/transport exception set, so it must propagate rather than come back as a failed
        // EmailResult — proving unrelated bugs aren't silently absorbed as routine send failures.
        await Assert.ThrowsAsync<ArgumentNullException>(
            () => sender.SendAsync(null!, "Subject", "Body", CancellationToken.None));
    }

    internal static IConfiguration ConfiguredWith(string host, int port, string fromAddress, string? username = null, string? password = null)
    {
        var values = new List<KeyValuePair<string, string?>>
        {
            new("Email:SmtpHost", host),
            new("Email:SmtpPort", port.ToString(CultureInfo.InvariantCulture)),
            new("Email:FromAddress", fromAddress),
        };

        if (username is not null)
        {
            values.Add(new KeyValuePair<string, string?>("Email:Username", username));
            values.Add(new KeyValuePair<string, string?>("Email:Password", password));
        }

        return new ConfigurationBuilder().AddInMemoryCollection(values).Build();
    }
}
