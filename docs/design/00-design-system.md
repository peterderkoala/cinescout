# CineScout design system

Resolves [wayfinder ticket #62](https://github.com/peterderkoala/cinescout/issues/62), part of the
[page-design reference set map](https://github.com/peterderkoala/cinescout/issues/61). This is the
shared visual language every other `docs/design/*.md` page doc builds on — page docs should link
here for anything covered below rather than restating it.

**Context**: CineScout is a self-hosted, single-user movie-tracking app. Every page today still
renders the unmodified Blazor-project-template look (`Home.razor` is the scaffold's "Hello,
world!", `NavMenu.razor` uses placeholder icon classes never wired to a real icon set) — this is
greenfield design work, not a redesign of an established identity.

## Foundation: Bootstrap 5.3

Stays on Bootstrap — already vendored at `src/cinescout.web/wwwroot/lib/bootstrap` (confirmed
**v5.3.3**), and every existing page's markup already uses its grid/utility classes. No functional
need (forms, lists, tabs, a seat-grid visualization) obviously outgrows it, and this is a
single-user personal tool with no need for a from-scratch component system.

Bootstrap 5.3 ships **native CSS-variable-based color-mode theming** (`--bs-*` custom properties,
toggled via a `[data-bs-theme="dark"]` selector) — this is the mechanism the dark-mode section
below builds on, not a hand-rolled alternative.

- Reference: [Bootstrap 5.3 docs](https://getbootstrap.com/docs/5.3/) — in particular
  [Customize → Color modes](https://getbootstrap.com/docs/5.3/customize/color-modes/) and
  [Customize → CSS variables](https://getbootstrap.com/docs/5.3/customize/css-variables/).

## Color

### Semantic colors: unchanged

Bootstrap's default semantic palette (`success`, `danger`, `warning`, `info`, `light`, `dark`,
`secondary`) stays as-is. It already maps cleanly onto CineScout's real states without inventing
new ones — e.g. `success`/`danger` for `Performance.Status` (Normal/Cancelled), sold-out/bookable
flags, or the Kinoheld circuit breaker's tripped state.

### Custom accents: two, both deliberately distinct from the semantic colors above

| Role | Light mode | Dark mode | Usage |
|---|---|---|---|
| **Primary** (deep burgundy/wine) | `#7A1F2B` | `#D97C88` | Nav highlights, primary buttons, links — the routine "primary action" color. |
| **Secondary accent** (marquee gold) | `#B8860B` | `#E0AC3F` | Reserved *only* for surfacing an actual `Match` (`RulesMatched`) — the app's real payoff moment. Never used for routine chrome. |

Both are chosen at a **deliberately different value/saturation from Bootstrap's own `danger`
(`#dc3545`) and `warning` (`#ffc107`)** — a bright saturated red or amber here would read as
"error"/"caution" rather than "brand accent" or "you have a match," colliding with Bootstrap's own
semantics. The burgundy and old-gold tones are darker/more muted specifically to avoid that
collision.

Light-mode primary (`#7A1F2B` with white text) is verified at **10.2:1 contrast** — well past the
WCAG AA 4.5:1 minimum for normal text. **Every other pairing above (dark-mode values, the
secondary accent in both modes, and any hover/active-state derivative a page doc introduces) must
be verified with a contrast checker before implementation** — treat the two values above as
starting points, not final-checked tokens.

- **Mandatory reference**: [WebAIM Contrast Checker](https://webaim.org/resources/contrastchecker/)
  — run every foreground/background pairing through this before shipping it.
- Reference: [WCAG 2.1 quick reference, 1.4.3 Contrast (Minimum)](https://www.w3.org/WAI/WCAG21/quickref/#contrast-minimum).

### Dark mode: both, via `prefers-color-scheme`, no manual toggle

Both light and dark are supported, selected automatically from the OS/browser's
`prefers-color-scheme` — no in-app toggle, no stored preference. This matters for CineScout
specifically because several pages are **static-SSR-only** (`Login`, `Setup`, `Schedule`,
`TrackedMovies`, `TimePreferences`, `SeatMatrices`, `PerformanceDetail` — no `@onclick`, no client
interactivity per root `CLAUDE.md`), so a manual toggle would need a POST round-trip or a
stored-preference cookie read on every static-SSR request. A pure-CSS approach avoids that
entirely and works identically regardless of a page's render mode.

**Mechanism**: Bootstrap's own dark-mode variables are scoped under `[data-bs-theme="dark"]`, but
since there's no JS toggle setting that attribute, mirror the same variable overrides under a
`@media (prefers-color-scheme: dark)` block targeting `:root` instead:

```css
/* app.css */
@media (prefers-color-scheme: dark) {
  :root {
    /* same --bs-* overrides Bootstrap defines under [data-bs-theme="dark"], plus: */
    --cinescout-primary: #D97C88;
    --cinescout-accent-match: #E0AC3F;
  }
}

:root {
  --cinescout-primary: #7A1F2B;
  --cinescout-accent-match: #B8860B;
}
```

- Reference: [MDN — `prefers-color-scheme`](https://developer.mozilla.org/en-US/docs/Web/CSS/@media/prefers-color-scheme).

## Typography

Two self-hosted Google Fonts — **no live Google Fonts CDN call**. A live CDN link means every page
load leaks the operator's IP/user-agent to Google, which cuts against this being a self-hosted,
privacy-leaning personal tool with no analytics or external trackers anywhere else in the stack.
Self-hosting avoids that and keeps the app working if Google Fonts is ever unreachable.

| Family | Role | Weights | Files |
|---|---|---|---|
| [Montserrat](https://fonts.google.com/specimen/Montserrat) | Headings (`h1`–`h6`), nav brand, buttons/labels | 600 (semibold), 700 (bold) | 2 `.woff2` |
| [Open Sans](https://fonts.google.com/specimen/Open+Sans) | Body text, dense data (film lists, seat grids, form content) | 400 (regular), 600 (semibold, for in-body emphasis) | 2 `.woff2` |

4 static `.woff2` files total. Both families are licensed under the
[SIL Open Font License](https://openfontlicense.org/) — self-hosting is standard and permitted.

**Self-hosting steps** (not yet done — this doc specifies the requirement, implementation is a
later effort per the map's docs-only scope):

1. Download the four weight/family combinations above from Google Fonts (or via
   [google-webfonts-helper](https://gwfh.mranftl.com/fonts), which pre-subsets to `.woff2` and
   generates the matching `@font-face` CSS — the recommended tool for this step).
2. Place the files under `src/cinescout.web/wwwroot/fonts/`.
3. Declare `@font-face` rules in `app.css` pointing at those local files (`font-display: swap` to
   avoid a flash of invisible text on first load).

**Type scale**: use Bootstrap's existing default scale (`h1`–`h6`, `.lead`, base body size)
unchanged — just split the `font-family` between the two tiers above (`h1`–`h6` and any
`.btn`/`.nav-link` get Montserrat; body copy, form inputs, and table/list content get Open Sans).
No new scale to design or maintain.

## Spacing

Bootstrap's default `$spacer`-based scale (`.m-*`/`.p-*` utilities, `0.25rem` steps) stays as the
**only** spacing vocabulary. No separate compact/dense tier is defined here — if the seat-matrix
visualization (shared between the Seat Matrices and Performance Detail page docs) turns out to
need tighter spacing than Bootstrap's smallest step (`0.25rem` / 4px) once it's actually designed,
that's decided in its own ticket, not pre-committed here before the need is concrete.

## Responsiveness

Bootstrap's default breakpoints (`sm` 576px, `md` 768px, `lg` 992px, `xl` 1200px, `xxl` 1400px),
authored **mobile-first** (base styles target phone, override upward via `min-width` media
queries) — Bootstrap's own convention, unchanged. Matches the actual usage pattern: a solo
operator checking schedules/matches from a phone as often as from a desktop at home.

- Reference: [Bootstrap 5.3 — Breakpoints](https://getbootstrap.com/docs/5.3/layout/breakpoints/).

## For page-doc authors

Every other doc in `docs/design/` should **link to this doc** for color/type/spacing/breakpoint
questions rather than restating them, and should only describe what's specific to that page: its
function, its layout, any component-level split, and page-specific solution references (e.g. a
particular Bootstrap component, or a visual-inspiration reference for that page's own layout).

## Solution references (summary)

Mandatory for any page/component doc that touches color or type:

- [Bootstrap 5.3 docs](https://getbootstrap.com/docs/5.3/), esp.
  [Color modes](https://getbootstrap.com/docs/5.3/customize/color-modes/) and
  [CSS variables](https://getbootstrap.com/docs/5.3/customize/css-variables/)
- [WebAIM Contrast Checker](https://webaim.org/resources/contrastchecker/)
- [MDN — `prefers-color-scheme`](https://developer.mozilla.org/en-US/docs/Web/CSS/@media/prefers-color-scheme)

Useful when self-hosting or extending typography:

- [Montserrat](https://fonts.google.com/specimen/Montserrat) /
  [Open Sans](https://fonts.google.com/specimen/Open+Sans) on Google Fonts
- [google-webfonts-helper](https://gwfh.mranftl.com/fonts) — self-hosted subsetting tool
- [SIL Open Font License](https://openfontlicense.org/)
