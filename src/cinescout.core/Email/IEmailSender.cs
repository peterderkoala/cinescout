namespace cinescout.core.Email;

public interface IEmailSender
{
    Task<EmailResult> SendAsync(string to, string subject, string body, CancellationToken cancellationToken);
}

/// <summary>
/// Outcome of one send attempt — never thrown, always returned, mirroring
/// <see cref="cinescout.core.Discord.NotificationResult"/>'s shape but kept as its own type so
/// <c>Email/</c> stays self-contained.
/// </summary>
public sealed record EmailResult(bool Success, string Detail);
