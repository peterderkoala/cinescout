# Schedule page design

Resolves [wayfinder ticket #66](https://github.com/peterderkoala/cinescout/issues/66), part of the
[page-design reference set map](https://github.com/peterderkoala/cinescout/issues/61). See
[`00-design-system.md`](00-design-system.md) for color/type/spacing/breakpoints,
[`layout-navigation.md`](layout-navigation.md) for the `MainLayout` shell this page renders inside,
and [`components/film-performance-card.md`](components/film-performance-card.md) for the shared
card this page's listing uses (compact-row variant).

**Current state**: `Schedule.razor` renders a single flat HTML `<table>` — one row per upcoming
performance, sorted purely chronologically across every film mixed together, no grouping, no date
cap, no filters. Columns: Film / Site-Room / When / Sold out / Seats. This doc replaces the table
with a grouped, capped, card-based listing.

**Render mode**: static-SSR-only (no `@onclick`, no client interactivity) — navigation between date
windows uses query params and plain links, the same pattern `TimePreferences`/`SeatMatrices`
already establish with `?tab=`/`?editId=`.

## Grouping: by day

Performances group under day headers ("Today," "Tomorrow," then a dated heading like "Fri, Aug
1") rather than staying a flat chronological list — matches how someone actually plans ("what can I
go see tonight/this weekend"). **Days with zero performances are silently skipped** — no empty-day
placeholder cluttering the list.

Within a day, **no further sub-grouping by film** — each showtime renders as its own separate
[compact-row card](components/film-performance-card.md#compact-row), even if the same film shows
multiple times that day (its title just repeats). Keeps the card's contract simple: one performance
in, one card out.

## Date range: capped, with navigation

Default window is the **next 14 days** from today, not the unbounded "everything upcoming" the
current query pulls. Forward/back navigation via query params (e.g. `?weekOffset=1` for the
following week), with "Next week →" / "← Previous week" links — consistent with this app's existing
static-SSR query-param-driven navigation pattern. Bounding the window keeps a single page load
predictable in size regardless of how far out Hall-of-Fame's crawl horizon extends.

## Card content

Each performance uses the [compact-row card](components/film-performance-card.md#compact-row)
as-is: film title (linking through to `/performances/{id}`, unchanged from today's behavior),
date/time, room, the mutually-exclusive status badge (`Cancelled`/`Sold out`/`Seats available`),
and the `Watching` indicator when the film is on the active Watched list. Both of the last two were
added to the shared component while resolving this ticket — Schedule was the page that surfaced
the need for them (mixing watched and unwatched films together in one list, and already showing a
seat-availability badge in the current implementation), so they're now part of the component's
spec, not one-off Schedule-only markup.

## Solution references

- [`components/film-performance-card.md`](components/film-performance-card.md) — the compact-row
  variant this page is built from
- [Bootstrap 5.3 — Grid/Stack](https://getbootstrap.com/docs/5.3/layout/grid/) (day-group layout)
- [`00-design-system.md`](00-design-system.md) and [`layout-navigation.md`](layout-navigation.md)
  — inherited, not restated here
