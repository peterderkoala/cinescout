namespace cinescout.model;

public class User
{
    public int Id { get; set; }
    public required string Username { get; set; }
    public string? PasswordHash { get; set; }
    public DateTimeOffset? SetupCompletedAt { get; set; }
}
