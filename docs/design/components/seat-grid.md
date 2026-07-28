# Component: Seat grid

Split out while resolving [wayfinder ticket #69](https://github.com/peterderkoala/cinescout/issues/69)
(Seat Matrices page design), part of the [page-design reference set map](https://github.com/peterderkoala/cinescout/issues/61).
Confirms the map's originally-flagged "seat-matrix visualization" reuse candidate. See
[`00-design-system.md`](../00-design-system.md) for color/spacing.

**Known consumers so far** (update this list as later page tickets confirm their usage):

- [`seat-matrices.md`](../seat-matrices.md) — **zone-preview mode** (this doc's primary spec).
- Performance Detail was flagged on the map as a likely second consumer, for showing **live seat
  availability** — a related but distinct mode (real `SeatStatus` data, not an abstract zone). Not
  yet confirmed; that page's own ticket (#70) decides and should extend this doc with a
  "live-availability mode" section rather than building a second, unrelated seat-grid from scratch.

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

## Solution references

- [Bootstrap 5.3 — Grid](https://getbootstrap.com/docs/5.3/layout/grid/) / [CSS Grid (MDN)](https://developer.mozilla.org/en-US/docs/Web/CSS/CSS_grid_layout)
  — either is a reasonable implementation basis for the rectangle; CSS Grid is likely the more
  direct fit for a dense uniform matrix of cells.
- [`00-design-system.md`](../00-design-system.md) — inherited, not restated here
