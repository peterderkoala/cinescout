# Component: Film/Performance card

Split out while resolving [wayfinder ticket #65](https://github.com/peterderkoala/cinescout/issues/65)
(Home page design), part of the [page-design reference set map](https://github.com/peterderkoala/cinescout/issues/61).
This is the shared way CineScout displays a `Film`/`Performance` pairing anywhere in the app — first
grounded in Home's Active-Matches hero, but intended to be referenced by any page doc that shows
film/performance content rather than each re-deriving it. See
[`00-design-system.md`](../00-design-system.md) for color/type/spacing.

**Known consumers so far** (update this list as later page tickets confirm their usage):

- [`home.md`](../home.md) — featured variant, Active Matches hero.
- Schedule, Watched Movies, Performance Detail were flagged on the map as likely consumers too
  (compact-row variant for the first two, featured variant probably for Performance Detail's own
  header) — not yet confirmed; each page's own ticket decides and should update this list and link
  back here rather than re-deriving the card from scratch.

## Two variants, one data model

Both variants show the same underlying `Film`/`Performance` data — they differ in density, not in
what data exists to show.

### Compact row

For dense browsing contexts with many performances at once (Schedule's full listing, Watched
Movies' per-film performance list). No poster — image weight doesn't scale to a long list.

Content, single line (wrapping to two on narrow viewports per the design system's mobile-first
breakpoints):

- Film title
- Date/time
- `Room` name
- A small status badge — `Sold out` (`.badge.text-bg-danger`), `Cancelled`
  (`.badge.text-bg-secondary`), or omitted entirely when bookable (no badge needed for the default/
  expected state — only flag the exceptions)

### Featured card

For spotlight contexts showing one performance with real weight (Home's hero Active Matches;
likely Performance Detail's own header, to be confirmed by that page's ticket).

Content:

- Poster image, left or top depending on container width (side-by-side on wider containers,
  stacked on narrow ones — mirrors the design system's mobile-first responsiveness). **Fallback**:
  a generic film-reel placeholder icon (Bootstrap Icons `bi-film`, sized to the poster's aspect
  ratio) when Hall-of-Fame/Kinoheld has no poster image for that film — not a broken-image icon or
  blank space.
- Film title (Montserrat, per the design system's heading treatment)
- `Site` / `Room`, date/time
- Price (the cheapest matching Kinoheld price category, per the notification content CLAUDE.md
  already documents for `RulesMatched` Discord embeds — this card reuses that same field, not a
  new one)
- **Match-reason badges** — small `.badge`s, not a prose sentence, so multiple cards scan quickly
  at a glance. Semantic-colored: a `success`-tinted badge (`.badge.text-bg-success`) for seat
  availability (e.g. `4 seats free`), a neutral badge (`.badge.text-bg-secondary`) for the
  satisfied time window (e.g. `Fri evening`). One badge per satisfied condition, not a single
  combined badge.
- Prominent booking CTA — `.btn.btn-primary` (the design system's burgundy), linking directly to
  the performance's Kinoheld `bookingLink`. **Opens in a new tab** (`target="_blank"`, with
  `rel="noopener"`) since it hands off to a third-party site — the user shouldn't lose their place
  in CineScout to complete a booking.

## Solution references

- [Bootstrap 5.3 — Badge](https://getbootstrap.com/docs/5.3/components/badge/)
- [Bootstrap 5.3 — Card](https://getbootstrap.com/docs/5.3/components/card/) (featured variant)
- [Bootstrap Icons](https://icons.getbootstrap.com/) — `bi-film` (poster fallback)
- [`00-design-system.md`](../00-design-system.md) — inherited, not restated here
