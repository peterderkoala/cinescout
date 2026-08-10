# Tracked Movies page design

Resolves [wayfinder ticket #67](https://github.com/peterderkoala/cinescout/issues/67), part of the
[page-design reference set map](https://github.com/peterderkoala/cinescout/issues/61). See
[`00-design-system.md`](00-design-system.md) for color/type/spacing/breakpoints,
[`layout-navigation.md`](layout-navigation.md) for the `MainLayout` shell this page renders inside,
and [`components/film-performance-card.md`](components/film-performance-card.md) for the shared
card the Tracked Films section uses (compact-row variant).

**Current state**: `TrackedMovies.razor` lists **every film ever crawled** — not just tracked ones
— in a single flat table, each row just a title and a Track/Untrack toggle button. No performance
context, no distinction between currently-programmed and long-expired films. This doc replaces it
with two sections.

**Render mode**: static-SSR-only. Mutations already go through Blazor's native `<EditForm
Model="this" FormName="tracked-movies">` with two differently-named submit buttons
(`trackFilmId`/`untrackFilmId`) per root `CLAUDE.md` — that mechanism is unchanged by this doc; only
the visual/content structure around it changes.

## Two sections

### 1. Your Tracked Films

The primary section — every film currently on the active Tracked list. For each:

- Film title as a section sub-heading, with a single **film-level Untrack action** next to it (not
  per-performance — untracking removes the whole film from the list, regardless of how many
  performances it has).
- **All** of that film's upcoming performances below the heading, each as its own
  [compact-row card](components/film-performance-card.md#compact-row) — not just the next one.
  This is the dedicated management page for tracked films, with more room than Home's abbreviated
  dashboard panel (which only shows the next performance per film); someone here is more likely
  asking "when can I actually go" across every option, not just "is anything happening at all."
- **Zero-performances state**: a film can be tracked with no upcoming performances at all (its run
  finished after being marked tracked — `TrackedMovie` only becomes *active* for matching while
  performances remain, per `CONTEXT.md`, but the row itself isn't auto-removed). Show this
  explicitly ("No upcoming performances") rather than leaving a blank gap under the film heading.

### 2. All films

The add-to-track list — every film **with at least one upcoming performance**, films whose entire
run has ended are excluded (tracking one can never produce a `Match`, so listing it here is just
clutter). Each row: film title + a `Track` button — plain, no compact-row card here, since there's
no single performance to attach the card to (a film row, not a film+performance pairing).

## Solution references

- [`components/film-performance-card.md`](components/film-performance-card.md) — compact-row
  variant used throughout Your Tracked Films
- [Bootstrap 5.3 — Card](https://getbootstrap.com/docs/5.3/components/card/) /
  [List group](https://getbootstrap.com/docs/5.3/components/list-group/) (section/row structure)
- [`00-design-system.md`](00-design-system.md) and [`layout-navigation.md`](layout-navigation.md)
  — inherited, not restated here
