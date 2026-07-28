# Home page: purpose and design

Resolves [wayfinder ticket #65](https://github.com/peterderkoala/cinescout/issues/65), part of the
[page-design reference set map](https://github.com/peterderkoala/cinescout/issues/61). See
[`00-design-system.md`](00-design-system.md) for color/type/spacing/breakpoints,
[`layout-navigation.md`](layout-navigation.md) for the `MainLayout` shell this page renders inside,
and [`components/film-performance-card.md`](components/film-performance-card.md) for the shared
card this page's hero section uses.

**Current state**: `Home.razor` is still the unmodified Blazor-template stub — `<h1>Hello,
world!</h1>` and nothing else. This doc replaces it entirely.

## Purpose: a dashboard, not a redirect or a static welcome page

Home becomes the "is there anything I need to act on right now?" screen. CineScout's real domain
already has the material for this — an active `Match` (a `Performance` that's satisfied a watched
film's preferences) is the app's actual payoff moment, so Home leads with exactly that rather than
being a dead first screen or a pass-through to Schedule.

## Layout, top to bottom

### 1. Hero: Active Matches

The dominant section — real visual weight, using the marquee-gold Match-accent from
`00-design-system.md` (reserved specifically for this). Each active `Match` renders as a
[**featured film/performance card**](components/film-performance-card.md#featured-card): poster,
`Site`/`Room`, date/time, price, match-reason badges, and a prominent booking CTA linking straight
to Kinoheld.

**Empty state** (the common case for a personal tool that mostly waits): a calm message — "No
matches yet — you'll see them here the moment a watched film's screening fits your preferences." —
with **no call-to-action**, since waiting is the normal state once at least one film is being
watched.

**Exception**: if there are also **zero active `WatchedMovie`s**, that's the one empty state that's
actually the user's next action, not just "waiting is normal" — show a contextual CTA pointing at
Watched Movies (e.g. "Start by marking a film as watched" linking to `/watched-movies`) instead of
the plain calm message.

### 2. Secondary panels: Watched Movies + Recent Activity

Two compact panels, **side-by-side on `md`+ screens** (matching the design system's mobile-first
breakpoints), **stacking to one column below `md`**:

- **Watched Movies** — a short list of active `WatchedMovie`s (capped to a handful, e.g. 5), each
  using the [**compact-row card**](components/film-performance-card.md#compact-row) treatment for
  its next upcoming performance if it has one, or just the film title if not. A "View all" link to
  `/watched-movies` below the list.
- **Recent Activity** — the most recent `NewFilmAdded` notifications (per `NotificationLog`,
  capped similarly, e.g. 5), each just the film title and a relative timestamp ("2 hours ago"). A
  "View all" link if a fuller activity view exists elsewhere — otherwise this panel is self-
  contained (this map doesn't currently plan a dedicated activity-history page; note this as a gap
  if one turns out to be wanted later, not something to resolve here).

## Solution references

- [`components/film-performance-card.md`](components/film-performance-card.md) — the hero and
  Watched Movies panel both consume this component; update its "known consumers" list once this
  page is actually built.
- [Bootstrap 5.3 — Grid](https://getbootstrap.com/docs/5.3/layout/grid/) (the two-column-on-`md`+
  secondary panel layout)
- [`00-design-system.md`](00-design-system.md) and [`layout-navigation.md`](layout-navigation.md)
  — inherited, not restated here
