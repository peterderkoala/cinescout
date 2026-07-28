# Component: Seat grid

Split out while resolving [wayfinder ticket #69](https://github.com/peterderkoala/cinescout/issues/69)
(Seat Matrices page design), part of the [page-design reference set map](https://github.com/peterderkoala/cinescout/issues/61).
Confirms the map's originally-flagged "seat-matrix visualization" reuse candidate. See
[`00-design-system.md`](../00-design-system.md) for color/spacing.

**Known consumers so far**:

- [`seat-matrices.md`](../seat-matrices.md) — **zone-preview mode**.
- [`performance-detail.md`](../performance-detail.md) — **live-availability mode**, confirmed while
  resolving [wayfinder ticket #70](https://github.com/peterderkoala/cinescout/issues/70).

## Zone-preview mode (confirmed by #69)

**What it's for**: making a `FavoriteSeatMatrix`'s defined zone ("rows A–D, seats 1–10") faster to
read than the equivalent sentence. **Not** a booking seat-map and **not** meant to depict a room's
real physical layout — CineScout doesn't have per-room seat-layout data independent of a crawled
performance anyway (`Room` rows come from Kinoheld's widget config — names only; real per-seat data
only exists per-`Performance`, from the seat crawl). Deliberately simpler and clearly distinct from
whatever Performance Detail's live-availability mode turns out to need.

**Rendering**: an **abstract rectangle** — as many rows as `RowStart`–`RowEnd` spans, as many
columns as `SeatNumberStart`–`SeatNumberEnd` spans. The entire rendered grid *is* the zone, filled
as one uniform highlighted block (burgundy primary, per the design system) — there's no surrounding
"room" context to show around it, since no real layout data backs this mode.

**Alongside, not replacing, text**: pairs with a shortened text line — `Party of 4` only. Row/seat
numbers become redundant once they're visible in the grid, but party size isn't visually obvious
from the grid alone and stays as text.

**Scale handling**: caps at roughly **15×15 rendered cells** at full legible detail. Beyond that,
keep the highlighted block's *proportions* accurate relative to its row/seat span, but don't force
individual cells to render below a legible minimum size — a very large zone (e.g. rows A–M × seats
1–20) should degrade gracefully into a proportionally-shaped block rather than 260 illegible tiny
squares.

## Live-availability mode (confirmed by #70)

**What it's for**: `PerformanceDetail`'s actual seat map for one performance — real per-seat data,
not an abstract preview. Replaces that page's previous per-row `Free / Total` count table entirely
(the top-line "X of Y seats free" summary text stays, since it's a useful at-a-glance number a grid
alone doesn't give as quickly).

**Layout — walk the adjacency chain, not raw seat numbers**: position cells within a row by
following each seat's real `LeftNeighborSeatId`/`RightNeighborSeatId` chain, **not** by
`SeatNumber` order. Kinoheld's own seat numbering isn't always gap-free/sequential, and the
matching engine's `SeatBlockFinder` already treats adjacency, not seat numbers, as the source of
truth for what counts as "contiguous" (per root `CLAUDE.md`). If the grid used raw numbers instead,
it could visually show a row as gap-free while the actual matching logic sees a gap — misleading on
exactly the screen where a user is judging whether a room has room for their group. This is real
implementation work (walking a linked structure instead of sorting a number), not just a rendering
default — flag it as such to whoever builds this.

**Cell coloring**, mapped from `SeatOccupancyStatus`:

| Status | Treatment |
|---|---|
| `Free` | Outlined/unfilled, `success`-bordered — the inviting, available state |
| `Sold` | Filled, muted `secondary` — visually receded, taken |
| `Other` | Outlined, `warning`-bordered (Bootstrap's stock warning yellow — **distinct** from the design system's custom marquee-gold Match-accent, so no collision with that reserved meaning) |

All three use Bootstrap's stock semantic colors, not the design system's custom accents — seat
status is a different kind of state than "this is a Match," and reusing the Match-gold here would
dilute that reserved meaning.

## Solution references

- [Bootstrap 5.3 — Grid](https://getbootstrap.com/docs/5.3/layout/grid/) / [CSS Grid (MDN)](https://developer.mozilla.org/en-US/docs/Web/CSS/CSS_grid_layout)
  — either is a reasonable implementation basis for the rectangle; CSS Grid is likely the more
  direct fit for a dense uniform matrix of cells.
- [`00-design-system.md`](../00-design-system.md) — inherited, not restated here
