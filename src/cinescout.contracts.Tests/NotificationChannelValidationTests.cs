using cinescout.model;

namespace cinescout.contracts.Tests;

public class NotificationChannelValidationTests
{
    private const string Webhook = "https://discord.com/api/webhooks/1290000000000000000/AbC-123_xyz";

    private static readonly NotificationChannelWriteRequest Valid = new()
    {
        Name = "seats", WebhookUrl = Webhook, Suppress = false, IsEnabled = true, Types = NotificationTypeFlags.All,
    };

    [Theory]
    [InlineData(Webhook, Webhook)]
    [InlineData("https://discordapp.com/api/webhooks/1290000000000000000/AbC-123_xyz", Webhook)]
    [InlineData("https://canary.discord.com/api/webhooks/1290000000000000000/AbC-123_xyz", Webhook)]
    [InlineData("https://ptb.discord.com/api/webhooks/1290000000000000000/AbC-123_xyz", Webhook)]
    [InlineData("https://discord.com/api/v10/webhooks/1290000000000000000/AbC-123_xyz", "https://discord.com/api/v10/webhooks/1290000000000000000/AbC-123_xyz")]
    [InlineData("  https://DISCORD.com/api/webhooks/1290000000000000000/AbC-123_xyz  ", Webhook)]
    public void NormalizeWebhookUrl_accepts_discord_hosts_and_rewrites_them_to_discord_com(string input, string expected) =>
        Assert.Equal(expected, NotificationChannelValidation.NormalizeWebhookUrl(input));

    [Theory]
    [InlineData("http://discord.com/api/webhooks/1290000000000000000/AbC")]
    [InlineData("https://evil.example/api/webhooks/1290000000000000000/AbC")]
    [InlineData("https://discord.com.evil.example/api/webhooks/1290000000000000000/AbC")]
    [InlineData("https://discord.com/api/webhooks/not-a-number/AbC")]
    [InlineData("https://discord.com/api/webhooks/1290000000000000000")]
    [InlineData("https://discord.com/api/webhooks/1290000000000000000/AbC?wait=true")]
    [InlineData("https://user@discord.com/api/webhooks/1290000000000000000/AbC")]
    [InlineData("https://discord.com:8443/api/webhooks/1290000000000000000/AbC")]
    [InlineData("http://[x")]
    [InlineData("")]
    public void NormalizeWebhookUrl_rejects_anything_that_is_not_a_discord_webhook(string input) =>
        Assert.Null(NotificationChannelValidation.NormalizeWebhookUrl(input));

    [Fact]
    public void Validate_valid_request_returns_null() =>
        Assert.Null(NotificationChannelValidation.Validate(Valid, webhookUrlRequired: true));

    [Fact]
    public void Validate_blank_name_returns_message() =>
        Assert.Equal("Name is required.", NotificationChannelValidation.Validate(Valid with { Name = " " }, webhookUrlRequired: true));

    [Fact]
    public void Validate_missing_url_is_only_an_error_when_required()
    {
        Assert.Equal("Webhook URL is required.", NotificationChannelValidation.Validate(Valid with { WebhookUrl = "" }, webhookUrlRequired: true));
        Assert.Null(NotificationChannelValidation.Validate(Valid with { WebhookUrl = null }, webhookUrlRequired: false));
    }

    [Fact]
    public void Validate_non_discord_url_returns_message() =>
        Assert.Equal("Webhook URL must be a Discord webhook URL (https://discord.com/api/webhooks/…).",
            NotificationChannelValidation.Validate(Valid with { WebhookUrl = "https://example.com/hook" }, webhookUrlRequired: false));

    [Theory]
    [InlineData("not a url")]
    [InlineData("ftp://cdn.example/a.png")]
    public void Validate_bad_avatar_url_returns_message(string avatar) =>
        Assert.Equal("Avatar URL must be a valid http(s) URL.",
            NotificationChannelValidation.Validate(Valid with { AvatarUrl = avatar }, webhookUrlRequired: true));
}
