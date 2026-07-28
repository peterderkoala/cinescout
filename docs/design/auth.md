# Auth pages: Login + Setup

Resolves [wayfinder ticket #64](https://github.com/peterderkoala/cinescout/issues/64), part of the
[page-design reference set map](https://github.com/peterderkoala/cinescout/issues/61). Covers
`Login.razor` and `Setup.razor`. See [`00-design-system.md`](00-design-system.md) for color/type/
spacing/breakpoints and [`layout-navigation.md`](layout-navigation.md) for the `MinimalLayout` shell
both pages now render inside (no nav, no navbar — just the wordmark and page content, centered).
This doc only covers what's specific to these two pages' own content.

**Current state**: both pages already share nearly identical structure — a left-aligned
`.row`/`.col-lg-6` block, `.form-floating` inputs, a plain `<p class="text-danger" role="alert">`
for errors, no card/panel, no logo/wordmark. Both are **static-SSR-only** by design (no `@onclick`,
no Blazor interactivity — their `POST` handlers call `HttpContext.SignInAsync` directly), a
constraint every decision below respects.

## Shared visual treatment

`Login` and `Setup` are deliberately **visually identical** — same card, same button styling, same
wordmark placement — differing only in their fields and copy. A first-run setup event doesn't need
its own accent color or badge to read as weightier than routine login; the token field and its
subtitle copy already signal that. This also sidesteps a real constraint: the marquee-gold accent
from `00-design-system.md` is reserved *only* for surfacing an actual `Match` — inventing a
"first-run" treatment would either collide with that reserved meaning or require a third accent
color the design system never scoped for.

## Layout, top to bottom

1. **Wordmark**, centered, above the card — not inside it as a card header. Keeps the card focused
   purely on the form; the brand identity sits outside it, matching `layout-navigation.md`'s
   navbar-brand styling (Montserrat 700 + `bi-film`) for visual continuity between the
   authenticated shell and these two pages.
2. **Card** (Bootstrap `.card`), centered, containing:
   - Page heading (`Log in` / `Set up CineScout`) — `Setup` keeps its existing muted subtitle
     explaining the token requirement.
   - Error alert, **if present**: upgraded from the current plain `<p class="text-danger">` to a
     Bootstrap `.alert.alert-danger` — a bare red line of text can get visually lost against the
     form fields below it inside a card; a bounded alert box (background tint + border, using
     Bootstrap's `danger` semantic color) reads more clearly as "something went wrong" before the
     user starts re-reading field labels. `role="alert"` carries over unchanged.
   - The form itself (fields below).
   - Submit button (`btn btn-primary`, uses the design system's burgundy primary).

## Password fields: visibility toggle

Add a show/hide toggle (eye icon, Bootstrap Icons — `bi-eye`/`bi-eye-slash` — now available per
`layout-navigation.md`) to every password field: `Login`'s single password field, and both of
`Setup`'s (`password` + `confirmPassword`). Most valuable on `Setup`, where a typo in either blind
password field is the direct cause of its existing "Passwords must match" error case.

Implementation note: this needs **no Blazor interactivity** — a small vanilla `<script>` toggling
the input's `type` attribute between `password`/`text` on click works on a static-SSR page exactly
like the rest of these pages' plain-HTML-form posture (`Login.razor`'s existing pattern, per root
`CLAUDE.md`). Each field's toggle button is a plain `<button type="button">` with an inline
`onclick`, not a Blazor `@onclick` handler.

## Field-level details (unchanged from current implementation)

- `.form-floating` input style stays — already in place on both pages, no reason to change.
- `Login`: single password field, `autocomplete="current-password"`, `autofocus`.
- `Setup`: token field (`autocomplete="off"`, `autofocus`) above the two password fields
  (`autocomplete="new-password"` on both).

## Solution references

- [Bootstrap 5.3 — Card](https://getbootstrap.com/docs/5.3/components/card/)
- [Bootstrap 5.3 — Alerts](https://getbootstrap.com/docs/5.3/components/alerts/)
- [Bootstrap 5.3 — Floating labels](https://getbootstrap.com/docs/5.3/forms/floating-labels/)
  (the `.form-floating` pattern already in use)
- [Bootstrap Icons](https://icons.getbootstrap.com/) — `bi-eye`/`bi-eye-slash` for the password
  toggle
- [`00-design-system.md`](00-design-system.md) and [`layout-navigation.md`](layout-navigation.md)
  — inherited, not restated here
