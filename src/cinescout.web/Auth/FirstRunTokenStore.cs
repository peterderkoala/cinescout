using System.Security.Cryptography;
using System.Text;

namespace cinescout.web.Auth;

/// <summary>
/// Holds the current first-run setup token in memory only, regenerated fresh on every application
/// boot. Concrete, not behind an interface (see <c>cinescout.web.Auth.AppUser</c>'s doc comment for
/// the same reasoning) — exactly one implementation, never swapped, and never persisted.
/// </summary>
public sealed class FirstRunTokenStore
{
    public string Token { get; } = Convert.ToHexString(RandomNumberGenerator.GetBytes(16));

    public bool Matches(string? submitted) =>
        submitted is not null
        && CryptographicOperations.FixedTimeEquals(
            Encoding.UTF8.GetBytes(Token),
            Encoding.UTF8.GetBytes(submitted));
}
