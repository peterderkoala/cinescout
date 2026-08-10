# API authentication, authorization, and CSRF defence for the WASM client's HTTP API

[Render mode per screen](https://github.com/peterderkoala/cinescout/issues/74) made all seven authenticated
screens `InteractiveWebAssembly` behind a new HTTP API ([HTTP API surface for the WASM client](https://github.com/peterderkoala/cinescout/issues/83)),
conditional on hardening that decision deferred to this ticket. As of this ADR, none of that API exists in
code yet — #83 and [Shared DTO project and entity-to-DTO mapping](https://github.com/peterderkoala/cinescout/issues/84)
are both paper decisions with no `Endpoints/` folder or DTOs written. This records the mechanism for each
mandated control, and the outcome of the questions #74 left genuinely open.

## Decisions

**Authorization coverage.** `AuthorizationOptions.FallbackPolicy` (`RequireAuthenticatedUser`) is
endpoint-routing-level, not Razor-Components-specific — it already covers any minimal API endpoint that
carries no explicit authorization metadata, with zero code change. New `/api` endpoints rely on it
implicitly, matching the existing style (the only explicit calls anywhere today are the two
`.AllowAnonymous()` opt-outs on `/account/login` and `/account/setup` — nothing explicitly opts in). This is
a recorded reliance, not a silent one.

**Antiforgery (CSRF).** Blazor's per-`EditForm` antiforgery (used by `TrackedMovies.razor`,
`PerformanceDetail.razor`) doesn't apply to JSON minimal APIs. The new `/api` endpoints use ASP.NET Core's
built-in `IAntiforgery` double-submit-cookie pattern, required by default via a convention on the `/api`
`MapGroup` for every non-GET request — a new endpoint added later under that group is protected without its
author doing anything special, satisfying #74's "fail closed" requirement. The WASM client gets its token
from the shell's initial server-rendered HTML: even WASM-rendered pages get one static-SSR pass to produce
the host page (`App.razor`/`Routes.razor`, rewritten under [Design tokens and the layout shell replacement](https://github.com/peterderkoala/cinescout/issues/76))
before the runtime boots, so `IAntiforgery.GetAndStoreTokens(HttpContext)` runs during that pass and the
token is embedded as a `<meta>` tag, read once via JS interop at WASM startup, and attached as a header on
every mutating `fetch`. A separate bootstrap endpoint was rejected — it would add a boot-time race between
the client's first mutating call and the token round-trip that embedding avoids entirely.

**`SameSite` on the auth cookie.** Set explicitly to `Strict`. Today it's an unrecorded framework default
(`Lax`); the WASM client and API are same-origin with no legitimate cross-site entry point into this
single-operator app, so `Strict`'s only cost — the cookie not riding along on a cross-site top-level GET
navigation into the app — is not a real scenario here.

**Kinoheld rate limiting and cooldown/breaker enforcement.** No generic `Microsoft.AspNetCore.RateLimiting`
middleware. `KinoheldSeatCrawlService.FetchForPerformanceAsync` already consults both
`KinoheldCircuitBreaker` and `KinoheldFetchCooldownTracker` internally — the Force Refresh endpoint calls
through to it unchanged and translates the returned `SeatFetchOutcome` (`CooldownActive`/`CircuitOpen`/etc.)
into `ProblemDetails` plus the `kinoheldStatus` extension #83 already specified. No duplicate enforcement is
needed at the endpoint layer for that path. Re-seed Rooms has no equivalent today — it's currently only
invoked once at startup — so it gets a new sibling tracker (per-cinema, ~5 minute window, longer than the
seat-fetch cooldown since a room re-seed is a heavier operation), following the same pattern rather than
introducing a second rate-limiting mechanism.

**Session expiry as an actionable status.** Plain `AddCookie()` here has no custom `Events`, so the default
`OnRedirectToLogin` unconditionally 302-redirects — `fetch()` follows it transparently and lands on the
`/login` HTML with a `200`, exactly the "opaque garbage" outcome #74 flagged. `Events.OnRedirectToLogin` and
`OnRedirectToAccessDenied` are overridden to detect the `/api` path prefix and return `401`/`403` instead of
redirecting, so the WASM client can catch the status and navigate to `/login` itself.

**Setup is confirmed untouched.** `Setup.razor` and `POST /account/setup` are both `[AllowAnonymous]`, gated
purely by `FirstRunTokenStore`'s constant-time compare — no session or cookie involved at any point. Nothing
here changes that.

## Considered Options

- **Custom token scheme instead of built-in `IAntiforgery`** (rejected): reinvents a solved problem in a
  first-party ASP.NET Core app for no benefit.
- **Antiforgery attached per-endpoint rather than via a group convention** (rejected): the exact failure mode
  #74 warned against — a new endpoint is unprotected by default until its author remembers to add the check.
- **Bootstrap endpoint (`GET /api/antiforgery-token`) for the WASM client's first token** (rejected): adds a
  boot-time race the shell-embedded token avoids; the static-SSR pass that produces the host page already
  happens on every load, so there's no extra cost to using it.
- **Generic `Microsoft.AspNetCore.RateLimiting` middleware for the two Kinoheld-triggering endpoints**
  (rejected): the existing cooldown-tracker pattern already solves this for Force Refresh with zero new code,
  and extending the same pattern to Re-seed Rooms keeps one enforcement family instead of two overlapping
  ones; a single-operator app has no per-IP abuse scenario a generic limiter would add value against.
- **`SameSite=Lax` (leave the current default)** (rejected): leaves cross-site top-level GET navigation as an
  unnecessary residual attack surface with no corresponding legitimate use case in this app.

## Consequences

- New `/api` endpoints (per #83/#84) rely on `FallbackPolicy` for authorization with no explicit
  `.RequireAuthorization()` calls, and get antiforgery protection automatically via the `MapGroup` convention
  — neither requires per-endpoint boilerplate.
- `WebServiceCollectionExtensions.AddCineScoutAuthentication` gains an explicit `SameSite = SameSiteMode.Strict`
  and a custom `Events.OnRedirectToLogin`/`OnRedirectToAccessDenied` pair keyed on the `/api` path prefix.
- A new tracker sibling to `KinoheldFetchCooldownTracker` is needed for Re-seed Rooms before
  [Sites screen: service seam, re-seed trigger, and delete semantics](https://github.com/peterderkoala/cinescout/issues/79)
  can implement that trigger.
- `CLAUDE.md`'s existing antiforgery paragraph — which justifies `EditForm`'s built-in protection for
  authenticated mutations — becomes wrong once these `/api` endpoints replace those Blazor-form flows on the
  seven WASM screens, and needs correction as part of that work.
- This unblocks [Sites screen: service seam, re-seed trigger, and delete semantics](https://github.com/peterderkoala/cinescout/issues/79).
