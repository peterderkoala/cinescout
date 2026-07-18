namespace cinescout.web.Auth;

/// <summary>
/// Marker type for <see cref="Microsoft.AspNetCore.Identity.IPasswordHasher{TUser}"/> — CineScout has exactly
/// one hardcoded user, so there's no user table to key the hasher on.
/// </summary>
public sealed class AppUser
{
    public static readonly AppUser Instance = new();

    private AppUser()
    {
    }
}
