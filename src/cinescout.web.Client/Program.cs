using cinescout.web.Client.Api;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using Microsoft.JSInterop;

var builder = WebAssemblyHostBuilder.CreateDefault(args);

builder.Services.AddAuthorizationCore();
builder.Services.AddCascadingAuthenticationState();
builder.Services.AddAuthenticationStateDeserialization();

// One shared HttpClient, same-origin (issue #86's resolution) — per-screen client wrappers
// (PingApiClient is the worked example) take this via constructor injection instead of their own
// AddHttpClient<T>(). No Microsoft.Extensions.Http.Resilience here: auto-retry on a user-initiated
// mutation would hide the transient failure the §8 error state is supposed to surface.
builder.Services.AddScoped(_ => new HttpClient { BaseAddress = new Uri(builder.HostEnvironment.BaseAddress) });
builder.Services.AddSingleton<AntiforgeryTokenStore>();
builder.Services.AddScoped<PingApiClient>();
builder.Services.AddScoped<CinemasApiClient>();
builder.Services.AddScoped<TimePreferencesApiClient>();
builder.Services.AddScoped<SeatMatricesApiClient>();
builder.Services.AddScoped<HomeApiClient>();
builder.Services.AddScoped<PerformanceDetailApiClient>();
builder.Services.AddScoped<TrackedMoviesApiClient>();
builder.Services.AddScoped<ScheduleApiClient>();

var host = builder.Build();

// Read once at startup, not per-request — no separate bootstrap endpoint (ADR 0004). The token
// comes from App.razor's server-rendered <meta> tag, already present in the DOM by the time this
// runs.
var antiforgeryTokenStore = host.Services.GetRequiredService<AntiforgeryTokenStore>();
var jsRuntime = host.Services.GetRequiredService<IJSRuntime>();
await using var antiforgeryModule = await jsRuntime.InvokeAsync<IJSObjectReference>("import", "./js/antiforgery.js");
antiforgeryTokenStore.Token = await antiforgeryModule.InvokeAsync<string?>("getAntiforgeryToken");

await host.RunAsync();
