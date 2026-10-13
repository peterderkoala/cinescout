# CLAUDE.md

Only gotchas and conventions you can't read off the code. Everything else: look at the source.

## Pointers

- Spec: [issue #14](https://github.com/peterderkoala/cinescout/issues/14); current work lives in open wayfinder maps.
- Glossary `docs/CONTEXT.md` (use its terms: `Performance`, `Cinema`, `TrackedMovie`, …), decisions `docs/adr/`.
- Design contract: `docs/design/Technical Design Spec.md` (outranks the remaining per-screen `.md`, #78); the
  `.dc.html` prototypes are its visual source.
- Projects and dependency graph: [README#architecture](README.md#architecture).
- `handoff/handoff.md` (git-ignored, overwritten in place): last session's state and next steps. Trust it
  only after checking `git log` since the commit it names; update it during a session, not just at the end.
- Hall-of-Fame API sample response: `docs/api/hall-of-fame/filtered-films.json`.

## Commands

- Build: `dotnet build src/cinescout.slnx`
- Test: `./run-tests.sh [--filter FullyQualifiedName~X]` — wraps `dotnet test`, falls back to `sg docker` if
  Docker isn't reachable directly. Core/web tests need Docker (Testcontainers Postgres).
- `./coverage.sh` — tests + coverage report, exactly what CI runs.
- Run: `dotnet run --project src/cinescout.web` (port 5100), needs `ConnectionStrings__Postgres`.
- Compose: `docker compose -f docker/docker-compose.yml up` with `docker/.env` copied from `.env.example`.
- No lint is configured — don't invent one.

## Project boundaries

- `cinescout.web.Client` never references `cinescout.persistence`/`cinescout.core` (EF/Npgsql don't belong in WASM).
- `cinescout.contracts` references only the dependency-free `cinescout.model`, so DTOs use model enums
  (`DaysOfWeekFlags`, `SeatOccupancyStatus`) directly. Keep `cinescout.model` free of package references:
  it ships to WASM through contracts.
- Mapperly `[Mapper]` classes live in `cinescout.web/Mapping/` (it sees both sides). Anything not genuinely
  1:1 is hand-written mapping.

## Render modes

- Set once in `App.razor`: global `InteractiveWebAssembly`, `prerender: false`. Don't add per-page `@rendermode`.
- `Login`, `Setup`, `Error` are `[ExcludeFromInteractiveRouting]` static SSR — they need `HttpContext`
  (Login/Setup for `SignInAsync`). Login/Setup post plain forms to minimal-API endpoints and use `@layout AuthLayout`.
- Every other screen is WASM and talks to `/api` through a per-screen client deriving from `ApiClientBase`.

## `/api` conventions

- The `MapGroup` carries `AntiforgeryEndpointFilter`; the client sends the token from App.razor's
  `<meta name="antiforgery-token">` as `X-CSRF-TOKEN` (needs the explicit `HeaderName` — JSON bodies, not forms).
- Unauthenticated/forbidden `/api` calls return 401/403, never a login redirect. **Gotcha**:
  `UseStatusCodePagesWithReExecute` must stay scoped off `/api` (`UseWhen`), or the re-executed request's path
  becomes `/not-found` and the 401 turns back into a 302.
- Kinoheld failures surface as `ProblemDetails` with a `kinoheldStatus` extension, parsed by `KinoheldStatusParser`.

## Time

- `Performance.StartsAt` is Berlin wall-clock stored as a UTC instant. Convert via `CinemaTimeZone` before
  comparing or displaying. DTOs carry Berlin-local `DateTime` (`Kind=Unspecified`), never `DateTimeOffset`.
- Npgsql `timestamptz` throws on a non-zero offset — call `.ToUniversalTime()` first.

## Kinoheld posture

- Honest `CineScout/1.0 (personal-use)` UA. 400/404 from the seat API are expected outcomes, not errors.
- `KinoheldCircuitBreaker` (in-memory singleton) trips on 403/429/anomalous and stops all polling until restart.
  The resilience retry deliberately excludes 403/429 so the breaker handles them.
- The 30s cooldown applies to force-refresh only; routine fetches are bounded by the freshness window.

## Testing conventions

- Real Postgres via Testcontainers, never a mocked `DbContext`. One container per test class: core tests that
  touch the DB take `IClassFixture<PostgresFixture>` and call `ResetAsync()` (TRUNCATE) in `InitializeAsync`.
- NSubstitute only at the HTTP-edge seams (`IHallOfFameClient`, `IKinoheldClient`, `IDiscordNotifier`). Matching
  logic is pure, tested without I/O. No bUnit.
- Web tests: pass the connection string with `UseSetting` in `ConfigureWebHost` — `ConfigureAppConfiguration` is
  too late, `Program.cs` reads it before `Build()`. The `Testing` environment skips Hangfire and migrations and
  doesn't require the connection string.

## Runtime gotchas

- Register recurring jobs via DI-resolved `IRecurringJobManager`, not static `RecurringJob` (it reads the unset
  `JobStorage.Current` and throws at startup).
- Startup-wiring changes must be run against a real Postgres — `dotnet test` won't catch them.
- `IDiscordNotifier` never throws and reads config at call time; unconfigured = clean failure result.

## Docker gotchas

- Postgres 18 mounts at `/var/lib/postgresql`, not `.../data` — the old path makes it refuse to start.
- The app runs non-root: `COPY --from=build` needs `--chown=$APP_UID:$APP_UID`, and
  `/home/app/.aspnet/DataProtection-Keys` is created and chowned before `USER` so its named volume isn't
  root-owned (auth cookies must survive restarts).
- Build context is the repo root; excludes live in `docker/Dockerfile.dockerignore`.

## Auth invariant

- A null `User.PasswordHash` is the only "not set up" signal (ADR 0001); setup goes through `/setup` with the
  token logged at startup.

## Agent skills

- Issue tracker: GitHub via `gh`; wayfinder conventions in `docs/agents/issue-tracker.md`.
- Domain docs: `docs/agents/domain.md`.
