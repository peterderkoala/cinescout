# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project status

CineScout has a real .NET 10 solution in `src/` (see `src/cinescout.slnx`). The [wayfinder map](https://github.com/peterderkoala/cinescout/issues/1) produced a full spec, sliced into tickets tracked from [issue #14](https://github.com/peterderkoala/cinescout/issues/14); implementation is in progress against that ticket list. Current projects:

- `src/cinescout.web` — ASP.NET Core host (hosted Blazor WASM). Cookie authentication gates the whole app by
  default (`AuthorizationOptions.FallbackPolicy`); render mode is set **per page** (`@rendermode
  InteractiveWebAssembly`), not globally — the `/login` page must render as static SSR so its POST handler can
  call `HttpContext.SignInAsync` directly, and a global render mode on `<Routes>` would force every page
  (including login) into WASM with no way to opt a single page back out.
- `src/cinescout.web.Client` — the WASM client project; interactive pages/layout live here. Pages that need
  direct server-side access (`CineScoutDbContext`, `HttpContext`) instead live in
  `src/cinescout.web/Components/Pages` as static-SSR-only components (no `@rendermode`) — `Login.razor` and
  `Schedule.razor` are the two examples so far; `cinescout.web.Client` deliberately never references
  `cinescout.persistence` (EF Core/Npgsql aren't WASM-appropriate to ship to the browser).
- `src/cinescout.core` — domain services: `HallOfFame/` (schedule crawl — `IHallOfFameClient`, upsert/
  cancellation-by-absence logic, the Hangfire recurring job) and `Kinoheld/` (room seeding — `IKinoheldClient`,
  parses the widget page's inline `dataLayer.push({...})` JSON via `Utf8JsonReader` token-matching, not a naive
  brace-counting scan, since the blob embeds raw SVG markup with braces/parens inside string values). Mapping
  via Mapperly hasn't been needed yet — the DTO→entity shapes so far are simple enough for plain code.
- `src/cinescout.model` — the EF Core entity set (`Site`, `Film`, `Performance`, `Room`, `SeatStatus`, etc. — see `CONTEXT.md` for the full glossary).
- `src/cinescout.persistence` — `CineScoutDbContext`, migrations, and the design-time factory.
- `src/cinescout.persistence.Tests` — xUnit + NSubstitute + Testcontainers-backed Postgres tests for the persistence layer.
- `src/cinescout.core.Tests` — xUnit + NSubstitute + Testcontainers-backed Postgres tests for `cinescout.core`'s
  crawl/seeding services, same pattern as `cinescout.persistence.Tests`.
- `src/cinescout.web.Tests` — xUnit + `Microsoft.AspNetCore.Mvc.Testing` (`WebApplicationFactory<Program>`)
  integration tests for the web host, e.g. the login/auth gate.

Build: `dotnet build src/cinescout.slnx`. Run the web app: `dotnet run --project src/cinescout.web` (serves on
`http://localhost:5100` by default). Run tests: `dotnet test src/cinescout.slnx` — the persistence tests spin up
a real Postgres container via Testcontainers, so Docker must be reachable (if `docker ps` reports a permission
error after a fresh `usermod -aG docker`, wrap the test command in `sg docker -c "..."` rather than waiting for
a new login session). Don't invent lint commands — none are configured yet.

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

No `Auth:PasswordHash` is checked into `appsettings.json` (an unset hash means login always fails, which is the
safe default). To log in locally, set `Auth__PasswordHash` to a hash produced by
`new PasswordHasher<AppUser>().HashPassword(AppUser.Instance, "<password>")` (`cinescout.web.Auth.AppUser`) —
e.g. via `dotnet user-secrets` or an env var. `Auth:SessionLifetimeDays` defaults to 30.

`cinescout.web` now needs a real Postgres to actually run (`dotnet run`, not `dotnet test`): set
`ConnectionStrings__Postgres` (e.g. `Host=localhost;Port=5432;Database=cinescout;Username=...;Password=...`) —
the app throws on startup if it's missing. Migrations apply automatically at startup via
`Database.MigrateAsync()`. Hangfire (recurring crawl jobs) uses the same connection string as its job storage.
The `"Testing"` hosting environment (set by `cinescout.web.Tests`' `WebApplicationFactory` for DB-independent
tests like the login gate) skips all of this — Postgres/Hangfire wiring, the connection-string requirement, and
the startup migration — so those tests don't need Docker at all; tests that *do* need real persistence (crawl
upsert, room seeding) construct `CineScoutDbContext` directly against a Testcontainers Postgres instead, the
same pattern `cinescout.persistence.Tests` already uses.

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
- Allow marking specific movies as "watched" (i.e. movies to track).
- Alert on "watched" movies when a screening matches the defined preferences (time, room, seating).
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
