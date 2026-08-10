# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project status

CineScout has a real .NET 10 solution in `src/` (see `src/cinescout.slnx`). The [wayfinder map](https://github.com/peterderkoala/cinescout/issues/1) produced a full spec, sliced into tickets tracked from [issue #14](https://github.com/peterderkoala/cinescout/issues/14); implementation is in progress against that ticket list.

`./handoff/` (git-ignored) is where an agent session's end-of-session handoff doc gets written — check
there first when picking up mid-project; it's the fastest way to see what the last session did and what's
next, without re-deriving it from git log/issues. Throwaway per session, not part of project history.

Current projects:

- `src/cinescout.web` — ASP.NET Core host (hosted Blazor WASM). Cookie authentication gates the whole app by
  default (`AuthorizationOptions.FallbackPolicy`); render mode is set **per page** (`@rendermode
  InteractiveWebAssembly`), not globally — the `/login` page must render as static SSR so its POST handler can
  call `HttpContext.SignInAsync` directly, and a global render mode on `<Routes>` would force every page
  (including login) into WASM with no way to opt a single page back out. `Endpoints/ApiEndpoints.cs` (#90) is
  the `/api` `MapGroup` convention ADR 0004 mandates: `AntiforgeryEndpointFilter` validates every non-GET/HEAD/
  OPTIONS request under that group by default, so an endpoint mapped later needs no per-endpoint boilerplate;
  `MapPingEndpoint`'s `POST /api/ping` is the worked example proving it end-to-end, not a real screen endpoint
  (#92–#98 add those). The auth cookie is `SameSite=Strict`, and `AddCineScoutAuthentication`'s
  `Events.OnRedirectToLogin`/`OnRedirectToAccessDenied` return `401`/`403` for `/api`-prefixed paths instead of
  redirecting — the WASM client needs a status code it can branch on, not a `200` login-page body. **Gotcha**:
  `UseStatusCodePagesWithReExecute` had to move behind `app.UseWhen(path doesn't start with /api, ...)` —
  left unscoped, it re-executes the pipeline against `/not-found` on that same 401/403, and the re-executed
  request's `HttpContext.Request.Path` is `/not-found`, not `/api/...` anymore, so the override above would
  silently miss it and fall back to redirecting (caught by an integration test expecting `401`, not `302`, that
  failed until this was added — see `cinescout.web.Tests/ApiHardeningTests.cs`). `IAntiforgery` is registered
  with an explicit `HeaderName` (`X-CSRF-TOKEN`) — required because the WASM client sends JSON, not
  form-encoded bodies, and `ValidateRequestAsync` only reads a header-carried token if one is configured.
  `Components/App.razor` embeds the antiforgery token as a `<meta name="antiforgery-token">` tag via
  `IAntiforgery.GetAndStoreTokens(HttpContext)` (the `HttpContext` cascading parameter, same pattern
  `TimePreferences.razor`/`SeatMatrices.razor` use) — no separate bootstrap endpoint.
- `src/cinescout.web.Client` — the WASM client project; interactive pages/layout live here. Pages that need
  direct server-side access (`CineScoutDbContext`, `HttpContext`) instead live in
  `src/cinescout.web/Components/Pages` as static-SSR-only components (no `@rendermode`) — `Login.razor`,
  `Setup.razor`, `Schedule.razor`, and `TrackedMovies.razor` are the examples so far; `cinescout.web.Client` deliberately never
  references `cinescout.persistence` (EF Core/Npgsql aren't WASM-appropriate to ship to the browser).
  `Api/` (#90) holds the WASM data-access plumbing every screen ticket builds against: one shared
  `HttpClient` registered in `Program.cs` (`BaseAddress` = the host origin), constructor-injected into
  lightweight per-screen client wrapper classes — `PingApiClient` (calling the worked-example `POST
  /api/ping` endpoint) is the one concrete pattern this ticket produced, not a real screen client.
  `AntiforgeryTokenStore` holds the token `Program.cs` reads once via JS interop
  (`wwwroot/js/antiforgery.js`) at WASM startup from `App.razor`'s server-rendered `<meta
  name="antiforgery-token">` tag — no separate bootstrap endpoint. `KinoheldStatusParser` is the pure,
  directly-tested mapping from the `kinoheldStatus` `ProblemDetails` extension member (`"breaker-open"`
  \| `"not-bookable"` \| `"gone"` \| `"cooldown"`, per issue #83's resolution) to Technical Design
  Spec.md §5.7's exact alert-class/copy table, with a generic fallback for anything without that
  member; reading the actual HTTP response is left to each caller, kept out of this pure function per
  #81's testing convention. `Shared/CardSkeleton.razor` is the shared loading-skeleton component (built
  on Bootstrap's `placeholder`/`placeholder-glow` utilities) every screen renders during the
  static-prerender pass, gated on `RendererInfo.IsInteractive` per issue #86's resolution — skip the API
  fetch while prerendering, fetch once the component goes interactive.
  `TrackedMovies.razor` is also the first page that *mutates* data from a static-SSR page — it uses Blazor's
  native `<EditForm Model="this" FormName="...">` + `[SupplyParameterFromForm]` (not a plain HTML form posting
  to a separate minimal-API endpoint, which is what `Login.razor` does) since this is an authenticated,
  state-mutating action where the framework's built-in antiforgery protection is worth having; `Login.razor`'s
  plain-form approach was a deliberate exception for that one anonymous, low-risk action, not the default
  pattern to copy. `Setup.razor` (#41) reuses Login's plain-form/separate-minimal-API shape rather than
  TrackedMovies's `EditForm`, even though setup *does* mutate the `User` row: antiforgery tokens defend a
  mutation an attacker rides via a victim's ambient authenticated-session cookies, and setup has neither an
  authenticated session nor any ambient cookie to ride — it's gated by its own out-of-band secret instead (the
  startup-log first-run token), which is a stronger, purpose-built control than a generic antiforgery token
  would be here. Multiple per-row actions (Track/Untrack) share one `EditForm` via two differently-named
  submit buttons (`name="trackFilmId"` / `name="untrackFilmId"`, each carrying the film id as its `value`) bound
  to two separate nullable `[SupplyParameterFromForm]` int properties, rather than a dynamic `FormName` per row.
  `TimePreferences.razor`, `SeatMatrices.razor`, and `PerformanceDetail.razor` (#21/#22) extend the same
  static-SSR patterns: in-page tabs are plain links carrying a query param (`?tab=overrides`) since static SSR
  has no `@onclick`; edit flows use `?editId=` with the editor pre-populated only on GET (checked via the
  cascading `HttpContext`) so posted form values win on submit; successful mutations end in
  POST-redirect-GET via `NavigationManager.NavigateTo`, validation failures re-render with a Bootstrap alert.
  The Seat Matrices General tab shows at most one `FilmId`-null matrix per room, so the editor blocks creating
  a second one (it would be unreachable in the UI — not editable, toggleable, or deletable).
  `Login.razor`/`Setup.razor` (#99) opt into `AuthLayout.razor` (`cinescout.web.Client/Layout/`) via `@layout`
  instead of silently inheriting `MainLayout`'s navbar — there's no "you're not signed in yet" nav to show on
  either page. The password show/hide eye toggle (`wwwroot/js/password-toggle.js`, referenced once from
  `App.razor`) is plain JS with `document`-level click delegation on `.password-toggle`/`data-target`, not a
  Blazor interactive island — #72's islands research ruled that out (any one island pulls the whole WASM
  runtime onto an otherwise-static-SSR page). `/account/login`/`/account/setup` themselves are untouched by
  #99 — same redirects, same query-param error codes; only the *rendered copy* for those states changed to
  match Technical Design Spec.md §5.8 exactly ("Invalid password." / "Passwords must match.").
- `src/cinescout.core` — domain services: `HallOfFame/` (schedule crawl — `IHallOfFameClient`, upsert/
  cancellation-by-absence logic, the Hangfire recurring job), `Kinoheld/` (room seeding — `IKinoheldClient`,
  parses the widget page's inline `dataLayer.push({...})` JSON via `Utf8JsonReader` token-matching, not a naive
  brace-counting scan, since the blob embeds raw SVG markup with braces/parens inside string values — the same
  fetch also captures `cinema.id` into `Cinema.KinoheldCinemaId`, which the seat crawl needs as its `cid`; plus,
  since #22, the seat crawl itself: `GetSeatsAsync` returns a typed `KinoheldSeatsResult`
  (Success/NotBookable/NotFound/Blocked/Anomalous — 400/404 are *expected* per-performance outcomes, never
  thrown), `KinoheldSeatCrawlService` runs the recurring tracked-films-only crawl and the on-demand/
  force-refresh path, and `KinoheldCircuitBreaker` is a deliberately in-memory singleton that trips on any
  403/429/anomalous response and stops **all** Kinoheld polling until an app restart (the v1 "manual reset");
  the 30s `KinoheldFetchCooldownTracker` applies to force-refresh only — routine stale-cache fetches are
  bounded by the freshness window instead, per #14's double-click-guard-only rationale; the HttpClient sends an
  honest `CineScout/1.0 (personal-use)` UA and its resilience retry deliberately excludes 403/429 so the
  breaker, not a retry loop, handles being blocked),
  `TrackedMovies/` (`TrackedMovieService` — track/untrack is a plain insert/delete of a `TrackedMovie` row, not
  a soft-delete/status flag; the model has no such field and `FilmId` carries a unique index, so re-tracking an
  already-tracked film or untracking one that isn't tracked are both defensive no-ops, not errors), and
  `Discord/` (`IDiscordNotifier` — a thin outgoing-webhook-only wrapper, never throws, always returns a
  `NotificationResult` so callers can log the attempt regardless of outcome; the webhook URL is read from
  config at call time, not baked into `HttpClient.BaseAddress` at DI-registration time, and an unconfigured URL
  is a clean "not configured" failure result rather than a crash — this is the expected, safe-by-default state
  until #19's own `.env` var, `Discord__WebhookUrl`, documented since #17, is actually filled in),
  `Email/` (`IEmailSender`/`EmailSender`, #42 — mirrors `IDiscordNotifier`'s posture exactly: MailKit under the
  hood, config read at call time, a fixed known-exception set mapped to a failed `EmailResult` rather than a
  broad catch, sends wrapped in a manually-built `Microsoft.Extensions.Resilience` pipeline since
  `AddStandardResilienceHandler()` is HTTP-only and SMTP isn't HTTP), and `Matching/` (#23 — the "payoff
  feature": `TimeWindowMatcher`/`SeatBlockFinder` are pure, no-I/O logic per #14's testing decision (the latter
  walks true seat adjacency via `SeatStatus.LeftNeighborSeatId`/`RightNeighborSeatId`, not merely consecutive
  seat numbers); `MatchEvaluationService` is the I/O orchestrator, hooked into `KinoheldSeatCrawlService` right
  after each seat crawl, creating an `Active` `Match` once per `(Performance, TrackedMovie)` pair and firing
  `RulesMatched`/`SeatAvailabilityChanged` (the latter only when `Match.HasSufficientSeats` flips, not on every
  seat-count change) via the same notify-and-log pattern `TrackedMovieService` established; `CinemaTimeZone`
  converts `Performance.StartsAt` — persisted as a UTC-offset instant via `DateTimeOffset.FromUnixTimeSeconds`
  but actually a Europe/Berlin wall-clock time — before comparing against `FavoriteTimeWindow`, which is why
  `Schedule.razor`/`PerformanceDetail.razor` also route their display through it now instead of the raw value).
  `src/cinescout.web/Mapping/RoomMapper.cs` (#89) is Mapperly's first real use — a `[Mapper]` static partial
  class mapping `Room` (`cinescout.model`) to `RoomDto` (`cinescout.contracts`); it lives in `cinescout.web`,
  not `cinescout.contracts`, because `cinescout.contracts` is a true leaf that can never see `Room`, and
  `cinescout.web` already references both. Later entity→DTO bridges should add further `[Mapper]` classes here
  following the same shape. Plain hand-written mapping code remains the rule for anything that isn't
  genuinely 1:1 (a join, or one of §6's derived values in `docs/design/Technical Design Spec.md`).
- `src/cinescout.contracts` (#89) — the shared DTO project: a true leaf referenced by `cinescout.web` and
  `cinescout.web.Client`, itself never referencing `cinescout.model`/`cinescout.persistence`/`cinescout.core`.
  Holds `RoomDto` (the 1:1 Mapperly proof), `FilmPerformanceCardModel` (Technical Design Spec §4.1's shared
  card shape — deliberately omits `variant`/`onSelect` from the prop table, since neither is server data: the
  former is a rendering choice the calling page makes, the latter a UI callback that can't cross the wire),
  `Weekday`/`DayCodeConverter` (the §6.3 day-code convention — a self-contained `[Flags]` enum mirroring
  `cinescout.model.DaysOfWeekFlags` bit-for-bit, since this project can't reference that type directly; callers
  that can see both convert with a plain `(Weekday)(int)value` cast), and `PerformanceDateTimeFormatting` (the
  timestamp convention: DTOs carry `DateTime` with `Kind=Unspecified`, Berlin-local, never `DateTimeOffset`,
  since Blazor WASM's "local time zone" is the visitor's browser, not the server's — the UTC→Berlin conversion
  itself stays the caller's job, done via `cinescout.core`'s `CinemaTimeZone`, since this project can't
  reference `cinescout.core` either).
- `src/cinescout.model` — the EF Core entity set (`Cinema`, `Film`, `Performance`, `Room`, `SeatStatus`, etc. — see `CONTEXT.md` for the full glossary).
- `src/cinescout.persistence` — `CineScoutDbContext`, migrations, and the design-time factory.
- `src/cinescout.persistence.Tests` — xUnit + NSubstitute + Testcontainers-backed Postgres tests for the persistence layer.
- `src/cinescout.core.Tests` — xUnit + NSubstitute + Testcontainers-backed Postgres tests for `cinescout.core`'s
  crawl/seeding services, same pattern as `cinescout.persistence.Tests`.
- `src/cinescout.web.Tests` — xUnit + `Microsoft.AspNetCore.Mvc.Testing` (`WebApplicationFactory<Program>`)
  integration tests for the web host, e.g. the login/auth gate, and (#90) `ApiHardeningTests` covering the
  `/api` antiforgery convention, the `401`/`403`-not-redirect overrides, and `SameSite=Strict`.
- `src/cinescout.contracts.Tests` — plain xUnit, no I/O: day-code round-trip tests, a `Weekday` ↔
  `DaysOfWeekFlags` bit-layout parity pin, and `RoomMapperTests`, which references `cinescout.web` (the same
  way `cinescout.web.Tests` does) purely to exercise the real `RoomMapper` rather than a test-only stand-in.
- `src/cinescout.web.Client.Tests` (#90) — plain xUnit, no I/O, no bUnit (per #81's convention): currently just
  `KinoheldStatusParserTests`, exercising all four §5.7 outcomes plus the generic fallback.

Build: `dotnet build src/cinescout.slnx`. Run the web app: `dotnet run --project src/cinescout.web` (serves on
`http://localhost:5100` by default). Run tests: `./run-tests.sh` (repo root) — a thin wrapper around `dotnet
test src/cinescout.slnx` that falls back to `sg docker -c "..."` automatically if Docker isn't reachable
directly (e.g. a `usermod -aG docker` that hasn't taken effect in the current shell yet); pass through extra
`dotnet test` args as needed, e.g. `./run-tests.sh --filter FullyQualifiedName~HallOfFameCrawlServiceTests`. The
persistence/core tests spin up a real Postgres container via Testcontainers, so Docker must be reachable one way
or the other. Don't invent lint commands — none are configured yet.

`pipelines/` is still an empty placeholder per `ARCHITECTURE.md` — not yet started.

`docker/` holds a working Compose deployment: `docker compose -f docker/docker-compose.yml up` (or `cd docker &&
docker compose up`) builds `app` from `docker/Dockerfile` (multi-stage: SDK build stage, ASP.NET Core runtime
final stage) and starts it alongside a `postgres:18` service, gated on Postgres's healthcheck. Copy
`docker/.env.example` to `docker/.env` (git-ignored — never commit the real file) and fill in real values first;
`docker-compose.yml` reads secrets from it via `env_file`, never hardcoded. The build context is the repo root
(`build.context: ..`, since a Dockerfile can't `COPY` from outside its context) — `docker/Dockerfile.dockerignore`
(not a root-level `.dockerignore`) excludes `bin/`/`obj`/etc from that context; BuildKit picks up a
Dockerfile-specific ignore file named `<dockerfile>.dockerignore` automatically. **Postgres 18's official image
expects a single volume mount at `/var/lib/postgresql`, not the pre-18 `/var/lib/postgresql/data`** — it now
places data in a major-version-specific subdirectory itself for `pg_ctlcluster` compatibility; mounting at the
old path makes the container refuse to start. `ASPNETCORE_ENVIRONMENT` is unset in the container, so it runs as
`Production` (not `Testing`), meaning the app requires `ConnectionStrings__Postgres`, runs startup migrations,
and starts Hangfire for real — verified live end-to-end (migrations applied, both services healthy, a real
login round-trip against the containerized app issued a valid auth cookie).

The `app` container runs as the base image's built-in non-root `app` user (`USER $APP_UID`), not root. Two
things that only bite under a fresh non-root container and are easy to miss if you're used to running as root:
(1) `COPY --from=build` needs `--chown=$APP_UID:$APP_UID`, or the published files stay root-owned; (2) any
volume mounted into the container starts out root-owned unless the image already has that directory, owned by
the target user, *before* the volume's first use — Docker copies a named volume's initial content (including
ownership) from whatever's already at the mount point in the image. This is exactly why
`/home/app/.aspnet/DataProtection-Keys` (ASP.NET Core's default DataProtection key-ring path, needed so auth
cookies survive a container restart instead of silently becoming unverifiable — caught by actually logging in,
restarting the container, and confirming the same cookie still worked) is `mkdir`+`chown`'d in the Dockerfile
*before* `USER $APP_UID`, and is a named volume in `docker-compose.yml`, not just an ad hoc directory.

Login credentials live in the persisted `User` table (see `CONTEXT.md`), seeded by migration with
`PasswordHash = null` — a null hash is the sole "not set up" signal, and login always fails until it's set,
the same safe default as before (see [ADR 0001](docs/adr/0001-user-table-seeded-with-null-password-hash.md)).
A fresh deployment is guided to `/setup` (`Login.razor` and `POST /account/login` both redirect there while
`User.PasswordHash` is null; `Setup.razor` and `POST /account/setup` redirect the other way, to `/login`, once
it's set — symmetric, no new middleware, no re-running setup later). `/setup` requires the current token, held
in memory only by the `FirstRunTokenStore` singleton (`cinescout.web.Auth`, no interface — exactly one
implementation, never swapped, never persisted), regenerated fresh on every boot while unset and logged via
Serilog at startup (`LogFirstRunTokenIfNeededAsync`, after the legacy-hash migration runs, so an
already-migrated deployment doesn't get a misleading "please set up" log line); the submitted token is checked
with `CryptographicOperations.FixedTimeEquals`, not plain string equality. Successful setup writes
`PasswordHash`/`SetupCompletedAt` and signs the operator in immediately via the same `SignInAsync` call path
`/account/login` uses. `Auth:PasswordHash`/`Auth__PasswordHash` is upgrade-only and deprecated: it's read
exactly once, at startup, only while `User.PasswordHash` is still null, and copied verbatim into the row (same
hasher, no re-hash) — after that it's never read again, so it's safe to remove from `.env`; for a fresh
deployment, use the `/setup` page instead of hand-generating a hash. `Auth:SessionLifetimeDays` defaults to 30.

`cinescout.web` now needs a real Postgres to actually run (`dotnet run`, not `dotnet test`): set
`ConnectionStrings__Postgres` (e.g. `Host=localhost;Port=5432;Database=cinescout;Username=...;Password=...`) —
the app throws on startup if it's missing. Migrations apply automatically at startup via
`Database.MigrateAsync()`. Hangfire (recurring crawl jobs) uses the same connection string as its job storage.
The `"Testing"` hosting environment (set by `cinescout.web.Tests`' `WebApplicationFactory`) skips Hangfire wiring
and the startup migration, and doesn't *require* a connection string (`AddPersistence`'s `requireConnectionString`
is `false`) — but it doesn't skip persistence entirely: any test whose request path touches the database (e.g.
`LoginTests`, which checks the seeded `User` row) still needs a real Testcontainers Postgres, supplied via
`WebApplicationFactory.ConfigureWebHost`'s `UseSetting("ConnectionStrings:Postgres", ...)` — plain
`ConfigureAppConfiguration` isn't early enough, since `Program.cs` reads the connection string into a local
variable before `Build()` runs. Tests with no such request path (e.g. the unauthenticated-redirect check) don't
need Docker at all. Tests that need real persistence outside the web host entirely (crawl upsert, room seeding)
construct `CineScoutDbContext` directly against a Testcontainers Postgres instead, the same pattern
`cinescout.persistence.Tests` already uses.

Recurring jobs must be registered via the DI-resolved `IRecurringJobManager` (`app.Services.GetRequiredService<IRecurringJobManager>().AddOrUpdate<T>(...)`),
**not** the static `RecurringJob.AddOrUpdate<T>(...)` facade — the static facade reads the legacy global
`JobStorage.Current`, which the modern `builder.Services.AddHangfire(...)` DI registration never sets, so it
throws `InvalidOperationException` at startup (caught by actually running the app against a real Postgres, not
just `dotnet build`/`dotnet test` — worth doing for any change that touches startup wiring).

## Project idea (from IDEA.md)

CineScout's purpose is to automate movie-going logistics:

- Frequently crawl a local cinema's homepage for currently screening movies.
- Frequently crawl the Kinoheld booking service for seating info related to a movie.
- Store each crawl result in a database.
- Support user-defined room rules (e.g. best rows in a specific theater room).
- Support favorite times and theater rooms.
- Allow marking specific movies as "tracked" (i.e. movies to track).
- Alert on "tracked" movies when a screening matches the defined preferences (time, room, seating).
- Provide a redirect link straight to the booking flow for a matching screening.

This implies the eventual system will need: scheduled/periodic crawlers or scrapers, persistent storage,
a rules/matching engine for preferences, and a notification mechanism.

### References

- api ref for films: https://kamp-lintfort.hall-of-fame.website/programm/api/filtered-films or vue-schedule.js as resource
- a sample json extract can be found under /docs/api/hall-of-fame/filtered-films.json

**json structure keys**

- films --> base array with screening infos
- films/performances --> screening dates of each film with link to www.kinoheld.de booking references   

## Intended repository structure (from ARCHITECTURE.md)

- `src/` — home of all `.slnx` and `.cs` files, organized into subprojects.
- `docker/` — home of `Dockerfile`, `.dockerignore`, `docker-compose.yml`.
- `docs/` — documentation in markdown, divided by purpose (e.g. an `api` subfolder for API docs).
- `pipelines/` — CI/CD pipelines for PR checks, builds, and releases.

Follow this layout when adding new files rather than introducing an alternative structure.

## Agent skills

### Issue tracker

Issues are tracked as GitHub Issues on peterderkoala/cinescout via the `gh` CLI. See `docs/agents/issue-tracker.md`.

### Domain docs

Single-context layout: CONTEXT.md + docs/adr/ at the repo root. See `docs/agents/domain.md`.
