namespace cinescout.web.Auth;

/// <summary>
/// Marker type for <see cref="Microsoft.AspNetCore.Identity.IPasswordHasher{TUser}"/>. CineScout's persisted
/// <c>User</c> table (see ADR 0001) holds exactly one row, so this stays a fixed marker rather than the hasher
/// being keyed on the entity itself.
/// </summary>
public sealed class AppUser
{
    public static readonly AppUser Instance = new();

    private AppUser()
    {
    }
}
