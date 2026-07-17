# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project status

CineScout has a real .NET 10 solution scaffold in `src/` (see `src/cinescout.slnx`), but no application logic
yet — the [wayfinder map](https://github.com/peterderkoala/cinescout/issues/1) has produced a full spec
(architecture, data model, ingestion/notification/auth design), but nothing from that spec has been implemented
in code. Current projects:

- `src/cinescout.web` — ASP.NET Core host (hosted Blazor WASM, global `InteractiveWebAssembly` render mode).
- `src/cinescout.web.Client` — the WASM client project; all pages/layout live here.
- `src/cinescout.core`, `src/cinescout.model`, `src/cinescout.persistence` — empty stub class libraries, not
  yet wired into the solution or given real content.

Build: `dotnet build src/cinescout.slnx`. Run the web app: `dotnet run --project src/cinescout.web` (serves on
`http://localhost:5100` by default). No test project exists yet — xUnit + NSubstitute is the locked choice
(see the map's Notes) but not yet scaffolded. Don't invent lint/test commands beyond these.

`docker/` and `pipelines/` are still empty placeholders per `ARCHITECTURE.md` — not yet started.

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
