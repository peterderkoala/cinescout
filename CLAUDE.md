# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project status

CineScout has a real .NET 10 solution in `src/` (see `src/cinescout.slnx`). The [wayfinder map](https://github.com/peterderkoala/cinescout/issues/1) produced a full spec, sliced into tickets tracked from [issue #14](https://github.com/peterderkoala/cinescout/issues/14); implementation is in progress against that ticket list. Current projects:

- `src/cinescout.web` — ASP.NET Core host (hosted Blazor WASM). Cookie authentication gates the whole app by
  default (`AuthorizationOptions.FallbackPolicy`); render mode is set **per page** (`@rendermode
  InteractiveWebAssembly`), not globally — the `/login` page must render as static SSR so its POST handler can
  call `HttpContext.SignInAsync` directly, and a global render mode on `<Routes>` would force every page
  (including login) into WASM with no way to opt a single page back out.
- `src/cinescout.web.Client` — the WASM client project; all pages/layout live here.
- `src/cinescout.core` — domain services (mapping via Mapperly); no business logic yet.
- `src/cinescout.model` — the EF Core entity set (`Site`, `Film`, `Performance`, `Room`, `SeatStatus`, etc. — see `CONTEXT.md` for the full glossary).
- `src/cinescout.persistence` — `CineScoutDbContext`, migrations, and the design-time factory.
- `src/cinescout.persistence.Tests` — xUnit + NSubstitute + Testcontainers-backed Postgres tests for the persistence layer.
- `src/cinescout.web.Tests` — xUnit + `Microsoft.AspNetCore.Mvc.Testing` (`WebApplicationFactory<Program>`)
  integration tests for the web host, e.g. the login/auth gate.

Build: `dotnet build src/cinescout.slnx`. Run the web app: `dotnet run --project src/cinescout.web` (serves on
`http://localhost:5100` by default). Run tests: `dotnet test src/cinescout.slnx` — the persistence tests spin up
a real Postgres container via Testcontainers, so Docker must be reachable (if `docker ps` reports a permission
error after a fresh `usermod -aG docker`, wrap the test command in `sg docker -c "..."` rather than waiting for
a new login session). Don't invent lint commands — none are configured yet.

`docker/` and `pipelines/` are still empty placeholders per `ARCHITECTURE.md` — not yet started.

No `Auth:PasswordHash` is checked into `appsettings.json` (an unset hash means login always fails, which is the
safe default). To log in locally, set `Auth__PasswordHash` to a hash produced by
`new PasswordHasher<AppUser>().HashPassword(AppUser.Instance, "<password>")` (`cinescout.web.Auth.AppUser`) —
e.g. via `dotnet user-secrets` or an env var. `Auth:SessionLifetimeDays` defaults to 30.

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
