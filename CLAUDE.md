# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project status

CineScout is in the pre-implementation/scaffolding stage: the repository currently contains only planning
documents and empty placeholder directories (`src/`, `docker/`, `docs/`, `pipelines/`). No `.sln`/`.csproj`
files, source code, dependency manifests, or test/build tooling exist yet. There are no build, lint, or test
commands to run at this point — do not invent them. When code is added, this file should be updated with the
actual commands.

The `.gitignore` is a Visual Studio / .NET template and `ARCHITECTURE.md` explicitly describes `src/` as home
to "slnx and cs files", so the intended stack is C#/.NET, but this has not been established in code yet.

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
