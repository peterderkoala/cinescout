namespace cinescout.web.Client.Api;

/// <summary>
/// Holds the antiforgery token read once via JS interop at WASM startup (<c>Program.cs</c>) from
/// App.razor's server-rendered <c>&lt;meta&gt;</c> tag. Registered as a singleton so every per-screen
/// API client wrapper can attach it to mutating requests via constructor injection, without each one
/// re-reading the DOM itself.
/// </summary>
public sealed class AntiforgeryTokenStore
{
    public string? Token { get; set; }
}
