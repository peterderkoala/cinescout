# Layout & Navigation shell

Resolves [wayfinder ticket #63](https://github.com/peterderkoala/cinescout/issues/63), part of the
[page-design reference set map](https://github.com/peterderkoala/cinescout/issues/61). Covers the
shared shell — `MainLayout.razor`/`NavMenu.razor` — that every authenticated page renders inside.
See [`00-design-system.md`](00-design-system.md) for color/type/spacing/breakpoints; this doc only
covers what's specific to the shell.

**Current state** (for context — none of this survives as designed): `MainLayout.razor` is the
unmodified Blazor template shell — a sticky 250px sidebar holding `NavMenu`, a top bar whose only
content is an "About" link to Microsoft's ASP.NET Core docs, and a content `<article>`. It applies
as the single default layout to **every** route (`Routes.razor`'s `AuthorizeRouteView
DefaultLayout`), including `Login`/`Setup` — so an unauthenticated visitor currently sees the full
sidebar, nav links to protected pages and all. `NavMenu.razor` references placeholder
`bi-house-door-fill-nav-menu`-style classes for icons; **Bootstrap Icons is not actually vendored
anywhere in the project** — those classes are dead.

## Two layouts, not one

`Routes.razor`'s single `DefaultLayout="typeof(Layout.MainLayout)"` is replaced by a per-page
choice between two layouts:

- **`MainLayout`** — the authenticated app shell (navbar + content), used by every page except
  Login/Setup: `Home`, `Schedule`, `TrackedMovies`, `TimePreferences`, `SeatMatrices`,
  `PerformanceDetail`.
- **`MinimalLayout`** (new) — no navbar, no nav links to protected pages. Just the app
  wordmark/brand and the page's content, centered on the page. Used by `Login`/`Setup` via an
  explicit `@layout MinimalLayout` directive on each.

Rationale: an unauthenticated visitor seeing a navbar full of links to "Tracked Movies"/"Time
Preferences" before logging in is mostly harmless for a single-user tool, but it's visual noise on
the one screen where focus matters most — and `Login`/`Setup` are already static-SSR-only while
the shell itself renders as interactive WASM, so a lighter layout for those two is the simpler
build too, not just the cleaner design call.

## App shell: top navbar, not sidebar

Replace the sidebar entirely with a **top navbar** — Bootstrap's standard `.navbar` +
`.navbar-expand-md`, collapsing to a hamburger menu below `768px` (the `md` breakpoint from
`00-design-system.md`, replacing the template's one-off `641px`). Content renders full-width below
it — see [Content framing](#content-framing).

Rationale: only 4 top-level nav items don't justify a permanently-reserved 250px sidebar column,
especially once at least one destination page (the seat-matrix grid) wants that width back for its
own content. A navbar is also the more idiomatic Bootstrap pattern for a small flat set of
top-level pages.

### Navbar structure, left to right

1. **Brand/wordmark** — "CineScout" set in Montserrat 700 (per `00-design-system.md`), paired with
   a small `bi-film` icon. Links to `/`. This is also **Home's only nav entry point** — "Home" is
   deliberately *not* a separate nav item; the brand link covers it, keeping the nav-item list to
   the four feature pages below.
2. **Nav items** (collapse into the hamburger menu below `md`):
   - Schedule — icon `bi-calendar3` (or `bi-film` if a calendar reads as scheduling-app-generic;
     pick whichever pairs better against the brand icon once both are placed together)
   - Tracked Movies — icon `bi-bookmark-star`
   - Time Preferences — icon `bi-clock`
   - Seat Matrices — icon `bi-grid-3x3`
3. **Logout** (right-aligned, outside the collapsing nav-item group so it stays reachable even
   collapsed) — icon `bi-box-arrow-right`, icon + "Logout" text.

**Logout has no backend yet** — there is currently no `/account/logout` endpoint or sign-out call
anywhere in the codebase (sessions just ride out their 30-day cookie lifetime). This doc specs the
navbar *design* including a Logout control; wiring an actual endpoint is separate future
implementation work, not part of this map.

### Icons: Bootstrap Icons, self-hosted, inline SVG

Use [Bootstrap Icons](https://icons.getbootstrap.com/) — free/MIT-licensed, the natural pairing
with Bootstrap 5 (same maintainers, matching visual weight). Self-host as **inline SVG**, not the
icon webfont: no extra font-file request, no icon-font blur/accessibility quirks, and each icon
inherits `currentColor` so it automatically follows the burgundy/gold accent colors and dark mode
from `00-design-system.md` without any extra styling.

- Reference: [Bootstrap Icons](https://icons.getbootstrap.com/) (browse/search the full set;
  download individual SVGs or the sprite as needed).
- Reference: [Bootstrap Icons — Blazor/Razor usage notes](https://icons.getbootstrap.com/#usage) —
  inline SVG is listed as a supported usage pattern directly.

## Content framing

`.container-fluid` at the shell level — **no max-width cap**. Rationale: the seat-matrix grid
(shared by `SeatMatrices`/`PerformanceDetail`) will likely want real width to lay out many
seats/columns; capping every page to the same width for that one page's sake would be backwards.
Individual pages remain free to add their own narrower wrapper locally (e.g. a form-heavy page
like Time Preferences might want a `max-width: 640px` column for readability) — that's a per-page
decision, not the shell's to impose.

## Cleanup implied by this rewrite

Not new decisions — just consequences of replacing the sidebar/top-row split with the navbar
above:

- **Remove** the dead "About → Microsoft docs" link entirely; it has no relevance to CineScout.
- **Restyle `#blazor-error-ui`** (the framework's unhandled-error banner) to use the design
  system's semantic tokens (Bootstrap's `--bs-warning`/`--bs-danger` variables) instead of its
  current hardcoded `lightyellow` background, which was never touched from the template default
  and doesn't follow light/dark mode at all.

## Solution references

- [Bootstrap 5.3 — Navbar](https://getbootstrap.com/docs/5.3/components/navbar/)
- [Bootstrap 5.3 — Breakpoints](https://getbootstrap.com/docs/5.3/layout/breakpoints/) (the `md`/
  768px collapse point)
- [Bootstrap Icons](https://icons.getbootstrap.com/)
- [`00-design-system.md`](00-design-system.md) — colors, type, spacing, dark mode (all inherited
  here, not restated)
