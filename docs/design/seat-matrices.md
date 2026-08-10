# Seat Matrices page design

Resolves [wayfinder ticket #69](https://github.com/peterderkoala/cinescout/issues/69), part of the
[page-design reference set map](https://github.com/peterderkoala/cinescout/issues/61). See
[`00-design-system.md`](00-design-system.md) for color/type/spacing/breakpoints,
[`layout-navigation.md`](layout-navigation.md) for the `MainLayout` shell this page renders inside,
and [`components/seat-grid.md`](components/seat-grid.md) for the shared zone-preview grid this
page's cards use.

**Current state**: `SeatMatrices.razor` is the app's actual in-page-tabs page (`.nav-pills` +
`?tab=overrides` — confirming that pattern belongs specifically here, not to Time Preferences, per
that page's own ticket #68). Two tabs: **General (per room)**, one card per `Room` showing its
general matrix or "No general preference set"; **Film-specific overrides**, one card per override
matrix. Every card currently describes its zone purely as text ("rows A–D, seats 1–10, party of
4") and shows enabled/disabled via a **disabled, non-interactive checkbox** used purely for
display. Editor is a shared `.card` below, unchanged in mechanism by this doc (text/number inputs
for row/seat ranges — a live interactive picker isn't feasible without JS on this static-SSR page).

## Cards get the zone-preview grid

Both General and Film-specific-override cards add the
[zone-preview seat grid](components/seat-grid.md#zone-preview-mode-confirmed-by-69), replacing the
numeric portion of the current text description. Each card keeps: room name (General) or film
title + room badge (Overrides), the grid, `Party of 4` (shortened text, row/seat numbers now
redundant), and the existing Edit/Enable-Disable/Delete actions.

## Enabled/disabled: badge, not a fake checkbox

Replace the current disabled, non-interactive `<input type="checkbox" checked disabled>` — which
visually implies you can click it, but can't — with a plain `Enabled`/`Disabled` badge
(`.badge.text-bg-success` / `.badge.text-bg-secondary`), consistent with the badge language used
throughout the other page docs (status badges on Schedule/Tracked Movies, match-reason badges on
Home). The actual toggle stays the existing `Enable`/`Disable` submit button below — this only
changes the passive display element, not the mutation mechanism.

## Kept as-is (no change from current implementation)

- Tab structure and `?tab=overrides` query-param navigation.
- The General tab's one-general-matrix-per-room constraint and its "No general preference set"
  placeholder when absent (no grid needed there — there's no zone to render).
- Shared editor card below, with its existing text/number inputs and the film-select field shown
  only on the Overrides tab.
- `opacity-50` treatment for disabled matrices' cards.

## Solution references

- [`components/seat-grid.md`](components/seat-grid.md) — the zone-preview grid this page confirms
  and is built from
- [Bootstrap 5.3 — Badge](https://getbootstrap.com/docs/5.3/components/badge/) — enabled/disabled
  badge
- [Bootstrap 5.3 — Pills navigation](https://getbootstrap.com/docs/5.3/components/navs-tabs/#pills)
  (existing tab structure, kept)
- [`00-design-system.md`](00-design-system.md) and [`layout-navigation.md`](layout-navigation.md)
  — inherited, not restated here
