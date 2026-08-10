// Read once at WASM startup (see AntiforgeryTokenStore / Program.cs) — App.razor's static-SSR pass
// embeds the token as a <meta> tag before the runtime boots, so there's no bootstrap round-trip.
export function getAntiforgeryToken() {
    return document.querySelector('meta[name="antiforgery-token"]')?.getAttribute('content') ?? null;
}
