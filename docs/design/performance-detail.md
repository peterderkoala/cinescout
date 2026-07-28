# Performance Detail page design

Resolves [wayfinder ticket #70](https://github.com/peterderkoala/cinescout/issues/70), the last
open ticket on the [page-design reference set map](https://github.com/peterderkoala/cinescout/issues/61).
See [`00-design-system.md`](00-design-system.md) for color/type/spacing/breakpoints,
[`layout-navigation.md`](layout-navigation.md) for the `MainLayout` shell this page renders inside,
[`components/film-performance-card.md`](components/film-performance-card.md) for the header (featured
variant), and [`components/seat-grid.md`](components/seat-grid.md) for the seat map (live-availability
mode, confirmed by this ticket).

**Current state**: `PerformanceDetail.razor` renders a plain `<h1>` + text header (film title,
Site/Room, date/time, sold-out badge — no poster or price), a direct "Book on Kinoheld" link
(already `target="_blank"`, matching `film-performance-card.md`'s existing spec), four possible
outcome messages (circuit-breaker tripped, not bookable, not found, cooldown active — only the
circuit-breaker one is a styled `.alert`, the rest are plain `<em>` text), a **per-row seat-count
table** (`Row` | `Free/Total` — aggregates, not individual seats), and a force-refresh button.

**Render mode**: static-SSR-only. Force-refresh already goes through `<EditForm>`, mechanism
unchanged by this doc.

## Header: the featured card

Replace the plain `<h1>` + text block with the
[featured film/performance card](components/film-performance-card.md#featured-card) — poster,
price, and the booking CTA all come with it "for free" (one extra query each), context this page
currently lacks entirely despite being the single most detail-rich screen in the app.

**Match/Watching state**, since this page shows *any* performance, not just Matches like Home's
hero:

- If the viewed performance is currently an active `Match` → show the **match-reason badges**
  (gold-accented), same as Home's hero.
- Else if the film is on the Watched list (but this specific performance isn't a `Match`) → show
  just the **`Watching` indicator** (burgundy bookmark), same as the compact-row card.
- Else (unwatched film, browsed casually) → neither.

These are mutually exclusive in practice — an active `Match` implies the film is watched, so at
most one of the two ever shows.

## Seat map: live-availability grid, not the count table

Replace the per-row `Free/Total` table entirely with the
[seat grid's live-availability mode](components/seat-grid.md#live-availability-mode-confirmed-by-70)
— real per-seat cells, colored by `SeatOccupancyStatus`, laid out by walking each seat's actual
adjacency chain rather than raw seat numbers (see that section for why — it's the same accuracy
requirement the matching engine already has). **Keep** the existing "X of Y seats free" summary
line above the grid; it's a useful at-a-glance number a grid alone doesn't give as quickly.

## Outcome messages: consistent alert treatment

Unify all four outcome states into `.alert` boxes instead of the current mix (one styled alert,
three plain `<em>` texts):

| Outcome | Treatment |
|---|---|
| Circuit breaker tripped | `.alert-warning` (unchanged — already correct) |
| Not currently bookable | `.alert-secondary` — an expected, routine state |
| No longer available on Kinoheld | `.alert-secondary` — also expected/routine |
| Refreshed too recently (cooldown) | `.alert-info` — transient, minor |

Same small consistency pass as `seat-matrices.md`'s checkbox→badge fix: these are all real states a
user needs to notice, and a plain `<em>` line is easy to skim past next to a styled alert box.

## Kept as-is (no change from current implementation)

- Force-refresh button and its `<EditForm>` mechanism, 30-second cooldown enforced server-side.
- "Book on Kinoheld" link already opens in a new tab, matching the featured card's own spec — no
  change needed there.

## Solution references

- [`components/film-performance-card.md`](components/film-performance-card.md) — featured-card
  header
- [`components/seat-grid.md`](components/seat-grid.md) — live-availability seat map
- [Bootstrap 5.3 — Alerts](https://getbootstrap.com/docs/5.3/components/alerts/) — outcome message
  treatment
- [`00-design-system.md`](00-design-system.md) and [`layout-navigation.md`](layout-navigation.md)
  — inherited, not restated here
