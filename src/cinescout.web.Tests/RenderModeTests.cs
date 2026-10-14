namespace cinescout.web.Tests;

// Regression for #121: no page declared a render mode, so every screen was served as static SSR
// and sat on its loading skeleton forever. A WASM page's response must carry the component marker
// blazor.web.js boots from; Login must not, since its form posts need a plain static-SSR page.
public sealed class RenderModeTests : IClassFixture<PostgresWebFactory>
{
    // Framework-internal marker JSON (<!--Blazor:{"type":"webassembly",...}-->) — pinned on purpose.
    private const string WasmMarker = "\"type\":\"webassembly\"";

    private readonly PostgresWebFactory _factory;

    public RenderModeTests(PostgresWebFactory factory) => _factory = factory;

    [Fact]
    public async Task SignedInScreen_IsRenderedAsWebAssembly()
    {
        var client = await _factory.CreateAuthenticatedClientAsync();

        var html = await client.GetStringAsync("/schedule");

        Assert.Contains(WasmMarker, html);
    }

    [Fact]
    public async Task LoginPage_StaysStaticSsr()
    {
        await _factory.SetSeededUserPasswordHashAsync(PostgresWebFactory.CorrectPassword);
        var client = _factory.CreateClient();

        var html = await client.GetStringAsync("/login");

        Assert.Contains("<form", html);
        Assert.DoesNotContain(WasmMarker, html);
    }
}
