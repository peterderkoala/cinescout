# CineScout — Technical Design Spec

Design → code contract for implementing `CineScout Prototype.dc.html` in
`peterderkoala/cinescout` (branch `dev`).

- **Design source of truth:** `CineScout Prototype.dc.html` (screens), `FilmPerformanceCard.dc.html`,
  `SeatGrid.dc.html` (components), `Design Manual.dc.html` (tokens), `data.js` (sample data shapes).
- **Model source of truth:** `src/cinescout.model/*.cs`. Where design and model disagree, **the model wins**;
  every mismatch known at time of writing is listed in §9.
- The prototype is a *static* design: buttons that mutate data are inert. Behaviour described here is the
  intent, not something the prototype executes.

---

## 1. How the design is annotated

Every screen root and every data-bound form field in the prototype carries machine-readable annotations:

| Attribute | Meaning | Example |
|---|---|---|
| `data-screen-label` | Human name of the screen | `data-screen-label="Cinemas"` |
| `data-entity` | Primary model class the screen edits/displays | `data-entity="Cinema"` |
| `data-field` | `Class.Property` this control binds to | `data-field="Cinema.CrawlBaseUrl"` |

`data-field` values use the **exact C# property names** from `src/cinescout.model/`. When implementing,
grep the design for `data-field="FavoriteSeatMatrix.` to find every control bound to that entity.
Visible labels are the *user-facing* wording and deliberately differ from property names
(e.g. label “Seat from” ↔ `SeatNumberStart`); the mapping tables below are authoritative.

**Vocabulary rule:** the UI word for `Cinema` is **“Cinema”** everywhere (nav item, filters, editor labels).
This has changed twice: the earliest draft said “Cinema”, a later draft removed it in favor of “Site” (recorded
here at the time as final), and the operator has now reversed that removal — “Site” is overloaded in web
development (website, deployment site, static site) where the domain always meant one physical cinema, so the
removal of “Cinema” was itself the mistake. “Cinema” is the permanent vocabulary; do not re-introduce “Site”.
See [ADR 0002](../adr/0002-rename-site-to-cinema-and-watchedmovie-to-trackedmovie.md) for the full reasoning.
`Room` is “Room” (sample data shows German room names “Saal 1…”, which is content, not vocabulary).

---

## 2. Design tokens

Defined once in the root `<helmet><style>` of the prototype; mirror them into the app's stylesheet.
Bootstrap 5.3 + Bootstrap Icons 1.11 are the base; dark mode is **CSS-only** via
`@media (prefers-color-scheme: dark)` overriding Bootstrap's `--bs-*` variables (no JS toggle, so
static/SSR rendering is identical).

| Token | Light | Dark | Use |
|---|---|---|---|
| `--cinescout-primary` | `#7A1F2B` | `#7A1F2B` | Primary buttons, navbar, active pill, selected row accent |
| `--cinescout-primary-hover` | `#5C1720` | `#5C1720` | Primary hover |
| `--cinescout-on-primary` | `#fff` | `#fff` | Text on primary |
| `--cinescout-accent-match` | `#B8860B` | `#D1AB5B` | Match accent, tracking star, zone seats, override hint icon, focus ring |
| `--cinescout-accent-match-text` | `#8A6508` | `#D1AB5B` | “MATCH” kicker text (contrast-safe) |
| `--cinescout-navbar-bg` | `#7A1F2B` | `#2C161A` | Navbar |
| `--cinescout-page-bg` | `#f7f5f3` | `#16181A` | Page ground |
| `--bs-success` | `#328168` | `#328168` | Free seats, “Seats available”, active cinema dot |

Type: `Montserrat` 600/700 for headings, `.btn`, `.nav-link`, `.navbar-brand`, `.card-header`, `.cs-heading`;
`Open Sans` 400/600 for body. Utility classes to port verbatim: `.cs-btn-primary`, `.cs-btn-outline`,
`.cs-pill`, `.cs-heading`.

Layout: every screen body is `container-fluid py-4` with `max-width:1536px; margin-inline:auto`.
The navbar spans the **full viewport width** (it is outside that container).
Focus is always `outline: 2px solid var(--cinescout-accent-match); outline-offset: 2px` — never the browser default.

Dark-mode caveat: `.btn-outline-secondary` is re-tinted neutral and `.cs-btn-outline` becomes neutral with a
gold hover; the light-mode `.cs-btn-outline` is bordeaux. Port both blocks or the dark theme loses contrast.

---

## 3. Navigation

Order is fixed: **Schedule · Tracked Movies · Time Preferences · Seat Matrices · Cinemas**, with Logout pinned
right. Brand mark links to Home.

| Item | Icon | Route | Entity |
|---|---|---|---|
| (brand) CineScout | `bi-film` | `/` | `Match` |
| Schedule | `bi-calendar3` | `/schedule` | `Performance` |
| Tracked Movies | `bi-bookmark-star` | `/tracked-movies` | `TrackedMovie` |
| Time Preferences | `bi-clock` | `/time-preferences` | `FavoriteTimeWindow` |
| Seat Matrices | `bi-grid-3x3` | `/seat-matrices` | `FavoriteSeatMatrix` |
| Cinemas | `bi-building` | `/cinemas` | `Cinema`, `Room` |
| Logout | `bi-box-arrow-right` | `/login` | `User` |

Active item: white, `font-weight:600`, 2px bottom border in `--cinescout-accent-match`. Idle: `rgba(255,255,255,.78)`.

---

## 4. Shared components

### 4.1 `FilmPerformanceCard` (`FilmPerformanceCard.dc.html`)

One component, two variants. Suggested Blazor name `FilmPerformanceCard.razor` with a `Variant` parameter.

| Prop | Type | Fed from |
|---|---|---|
| `variant` | `compact` \| `featured` | caller |
| `title` | string | `Film.Title` |
| `cinema` | string | `Cinema.Name` (featured only) |
| `room` | string | `Room.Name` |
| `datetime` | string | `Performance.StartsAt`, pre-formatted (§7) |
| `price` | string | cheapest `PerformancePriceArea.OrderPrice` for the performance, currency-formatted |
| `status` | `none` \| `available` \| `soldout` \| `cancelled` | derived (§6.1) |
| `tracking` | bool | a `TrackedMovie` row exists for `Performance.FilmId` |
| `isMatch` | bool | an **Active** `Match` exists for this performance |
| `matchReasons` | string[] | derived (§6.2) |
| `posterSlot` / poster image | string | `Film.PosterUrl` (design uses a drop-slot placeholder; implementation uses the URL, falls back to the `bi-film` tile when null) |
| `bookingLink` | string | `Performance.BookingLink` |
| `onSelect` | callback | navigate to Performance Detail |

Rules: the featured card's CTA **“Book on Kinoheld”** is always the filled primary style (never outline);
`target="_blank" rel="noopener"`. A match card gets a 4px gold left border plus a gold-tinted ring and the
uppercase “MATCH” kicker. The tracking star (`bi-bookmark-star-fill`, gold) shows on compact rows and on
featured cards that are *not* matches.

### 4.2 `SeatGrid` (`SeatGrid.dc.html`)

| Mode | Fed from | Rendering |
|---|---|---|
| `zone` | one `FavoriteSeatMatrix` | Gold blocks, 16px cells, 2px gaps. Grid is `RowEnd−RowStart+1` × `SeatNumberEnd−SeatNumberStart+1`. |
| `live` | `SeatStatus` rows of one `Performance` | 26px cells, 4px gaps, plus “{free} of {total} seats free”. |

**Zone capping is normative:** never draw more than **5 rows × 25 seats**. Overflow is one 35%-opacity ghost
row/column plus a gold caption `+N rows · +N seats` (singular/plural per the prototype). This keeps every card
in the grid the same height regardless of auditorium size.

Live cell styles map 1:1 to `SeatOccupancyStatus`:

| `SeatOccupancyStatus` | Cell | Legend |
|---|---|---|
| `Free` | transparent fill, 2px `--bs-success` border | “Free” |
| `Sold` | filled `--bs-secondary` | “Sold” |
| `Other` | transparent fill, 2px `--bs-warning` border | “Other” |

A seat not present in the payload renders `visibility:hidden` (keeps the grid rectangular).

---

## 5. Screens

### 5.1 Cinemas — `data-entity="Cinema"`

Master/detail: cinema list (`col-lg-4`) + detail card and rooms card (`col-lg-8`).

**List row** — per `Cinema`, ordered by `Name`:
| Element | Source |
|---|---|
| Status dot | `Cinema.IsActive` → `--bs-success` : `--bs-border-color` |
| Bold line | `Cinema.Name` |
| Muted line | `Cinema.ExternalCinemaId` |
| Right badge | `Room` count for the cinema → `“{n} rows”`-style text `“{n} rooms”` / `“No rooms”` |
| Selected style | 3px left border `--cinescout-primary` + `rgba(122,31,43,.06)` background |

**Detail form**:
| Control | `data-field` | Notes |
|---|---|---|
| Name | `Cinema.Name` | required |
| External cinema id | `Cinema.ExternalCinemaId` | required; helper text “Slug used by the Hall-of-Fame schedule API.” |
| Crawl base URL | `Cinema.CrawlBaseUrl` | required, `type=url` |
| Kinoheld cinema id | `Cinema.KinoheldCinemaId` | **read-only** — set by the widget-config fetch. Non-null → value + green `bi-check-circle-fill` hint “Resolved from the widget config — used as cid for seat availability.” Null → placeholder text “Not resolved yet” + muted `bi-hourglass-split` hint “Resolves on the first successful widget-config fetch.” |
| Crawling switch | `Cinema.IsActive` | label “Crawling enabled” / “Crawling paused” |
| Header badge | `Cinema.IsActive` | `text-bg-success` “Active” / `text-bg-secondary` “Inactive” |
| “Last crawl” caption | *not in model* — see §9 |

Actions: **Save cinema** (primary), **Re-seed rooms** (`bi-arrow-repeat`, re-runs the widget-config fetch →
upserts `Room` rows and `Cinema.KinoheldCinemaId`), **Delete cinema** (outline danger, right-aligned).
**Add cinema** (primary, screen header) opens the same form empty.

**Rooms card** — read-only table of `Room` where `Room.CinemaId == Cinema.Id`:
columns `Room.Name`, `Room.ExternalAuditoriumId`, plus a per-row **Rename** action (only `Name` is user-editable;
`ExternalAuditoriumId` is provider-owned). Empty state: “No rooms seeded yet — rooms appear after the first
successful widget-config fetch.”

### 5.2 Seat Matrices — `data-entity="FavoriteSeatMatrix"`

Two pills: **General (per room)** = `FilmId is null`; **Film-specific overrides** = `FilmId is not null`.
Both share one filter bar and one editor card.

**Filter bar** (client-side, applies to both tabs):
`Cinema` select (`all` + every `Cinema.Name`) · `Room` select (`all` + `Room.Name` of the selected cinema;
resets to `all` when the cinema changes and the room no longer exists) · **Clear filters** (only while a filter
is set) · right-aligned count “Showing {n} of {total}”. Zero results renders the centered empty card
“No seat matrices match the selected cinema and room.” with a Clear-filters button.

**General tab** iterates **every `Room`**, not every matrix — a room without a matrix still gets a card:
| Element | Source |
|---|---|
| Kicker | `Cinema.Name` · `Room.Name` |
| Heading | `FavoriteSeatMatrix.Name` |
| Gold `bi-info-circle-fill` | shown when ≥1 override exists for the room; `title` = “Overridden by a film-specific matrix for: {film titles}” — this is the visual carrier of the *film-specific-beats-general* precedence rule |
| Badge | `IsEnabled` → `text-bg-success` “Enabled” / `text-bg-secondary` “Disabled”; whole card `opacity-50` when disabled |
| Zone grid | §4.2 |
| Meta strip | three columns separated by 1px vertical rules, 18px padding: **Rows** `{RowStart}–{RowEnd}` · **Seats** `{SeatNumberStart}–{SeatNumberEnd}` · **Party** `of {PartySize}` |
| Actions | Edit · Enable/Disable (label is the *action*, i.e. inverse of `IsEnabled`) · Delete (icon, outline danger) |
| No matrix | “No general preference set” + **Add matrix** outline button |

**Overrides tab** cards are identical except the heading is `Film.Title` with `FavoriteSeatMatrix.Name` below
as a light badge, and there is no override-hint icon.

**Editor card** (`max-width:44rem`):
| Control | `data-field` | Notes |
|---|---|---|
| Film (overrides tab only) | `FavoriteSeatMatrix.FilmId` | absent on the General tab → `null` |
| Cinema | `Room.CinemaId` | narrows the Room list; not persisted on the matrix itself |
| Room | `FavoriteSeatMatrix.RoomId` | |
| Name | `FavoriteSeatMatrix.Name` | **required** (model is `required string`); placeholder “e.g. Sweet spot” |
| Row from / Row to | `RowStart` / `RowEnd` | free text, single letters, compared lexicographically |
| Seat from / Seat to | `SeatNumberStart` / `SeatNumberEnd` | `type=number` |
| Party size | `PartySize` | `type=number` |

`IsEnabled` has no editor control — it is toggled from the card. New matrices default to `IsEnabled = true`.
Title of the card reads “New general matrix” / “New film override” per tab.

### 5.3 Time Preferences — `data-entity="FavoriteTimeWindow"`

Editor card + list, `max-width:32rem`. Seven toggle buttons `Mo Tu We Th Fr Sa Su` bound to
`FavoriteTimeWindow.DaysOfWeek` (§6.3); selected = primary fill, idle = tertiary background.
`Start`/`End` are `type=time` bound to `StartTime`/`EndTime` (`TimeOnly`). Save button label switches to
“Update window” with a Cancel button while editing; the row being edited is tinted `rgba(122,31,43,.06)`.
Validation shown in the design: “End time must be later than start time.” (`alert-danger`).
List rows show the day flags as primary badges plus `{StartTime}–{EndTime}` (en dash) with Edit / Delete.

### 5.4 Schedule — `data-entity="Performance"`

14-day window with Previous/Next week paging; caption `“{Mon d} – {Mon d} · next 14 days”`.
Days are grouped by date (uppercase muted heading; “Today”/“Tomorrow” for offset 0), **days with no
performances are omitted entirely**, and each group is a bordered rounded panel of compact cards.
Query: `Performance` joined to `Film`/`Room`, `StartsAt` inside the window, ordered by `StartsAt`.

### 5.5 Tracked Movies — `data-entity="TrackedMovie"`

Per `TrackedMovie` (joined to `Film`): title + **Untrack** (outline danger), then its upcoming performances
as compact cards, or “No upcoming performances”. Below, **All films** — `Film` rows with at least one
upcoming performance and no `TrackedMovie` — each with a **Track** button.

### 5.6 Home — `data-entity="Match"`

Featured cards for `Match.Status == Active` (two-column). Empty states, exactly as worded:
no matches and nothing tracked → “No matches yet — start by marking a film as tracked.” (link to Tracked);
no matches but films tracked → “No matches yet — you'll see them here the moment a tracked film's screening
fits your preferences.” Below: **Tracked Movies** panel (max 5, compact rows, “View all →”) and
**Recent Activity** (last 5 `NotificationLog` rows → `{Film.Title}` + relative `SentAt`).

### 5.7 Performance Detail — `data-entity="Performance"`

Back-link, featured card, then the **Seats** section: `Force refresh` (`bi-arrow-clockwise`) and the live
`SeatGrid` with its three-item legend. Outcome alerts (design copy is normative):

| Case | Alert | Copy |
|---|---|---|
| Circuit breaker open | `alert-warning` | “Live seat data is temporarily unavailable — the Kinoheld connection's circuit breaker is open. Retrying automatically.” |
| `IsBookable == false` | `alert-secondary` | “This performance isn't currently bookable on Kinoheld.” |
| Gone from source | `alert-secondary` | “This performance is no longer available on Kinoheld.” |
| Refresh cooldown | `alert-info` | “Refreshed too recently — force refresh is available again in 30 seconds.” |

### 5.8 Login / Setup — `data-entity="User"`

Centered `max-width:24rem` card on a full-height ground, brand mark above. Login: floating password field
with a show/hide eye toggle, full-width primary button, “First run? Set up CineScout”.
Setup: setup token + password + confirm (`User.PasswordHash` after hashing; completing it stamps
`User.SetupCompletedAt`), errors “Invalid password.” / “Passwords must match.”

---

## 6. Derived values (design ↔ model, non-1:1)

### 6.1 Performance status badge
Precedence, first match wins:

| Condition | Badge |
|---|---|
| `Performance.Status == Cancelled` | `text-bg-secondary` “Cancelled” |
| `IsSoldOut` | `text-bg-danger` “Sold out” |
| enough contiguous free `SeatStatus` seats for the applicable matrix (`Match.HasSufficientSeats`) | `text-bg-success` “Seats available” |
| otherwise | no badge |

Note the design's “Seats available” is *not* “any free seat” — it is the matrix/party-size check.

### 6.2 Match reasons
Short strings on featured cards. Seat-related reasons (`/seat|free/i`) are green, time-related grey.
Generate: `“{n} seats free”` from the contiguous-block check that set `Match.HasSufficientSeats`, and
`“{Day} {part-of-day}”` from the `FavoriteTimeWindow` that matched. Keep them ≤3 words.

### 6.3 `DaysOfWeekFlags` ↔ day chips
`Monday→Mo, Tuesday→Tu, Wednesday→We, Thursday→Th, Friday→Fr, Saturday→Sa, Sunday→Su`, always in that order.
The UI is a multi-select over one flags value, not seven rows.

### 6.4 Applicable matrix for a performance
`FavoriteSeatMatrix` where `RoomId == Performance.RoomId` and (`FilmId == Performance.FilmId` **or**
`FilmId is null`), `IsEnabled` only; the film-specific one wins. When `Performance.RoomId` is null (no seat
crawl yet) no matrix applies and no seat badge is shown.

---

## 7. Formatting

| Value | Format | Example |
|---|---|---|
| Performance date+time (lists) | `ddd, MMM d · HH:mm` | `Fri, Aug 1 · 20:15` |
| Time inside a day group | `HH:mm` | `20:15` |
| Week caption | `MMM d – MMM d` (en dash) | `Aug 1 – Aug 14` |
| Row / seat ranges | `{start}–{end}` (en dash, no spaces) | `A–D`, `1–10` |
| Party size | `of {n}` | `of 4` |
| Price | currency with symbol | `€9.50` |
| Relative activity time | “2 hours ago”, “1 day ago” | |
| Separator between cinema and room | ` · ` (middle dot, spaced) | `Kino am Rathaus · Saal 2` |

---

## 8. States every screen must implement

Loading (skeleton or the design's placeholder counts), empty (copy above — never an unstyled blank),
error (Bootstrap alerts, copy in §5.7), disabled (`opacity-50` card + secondary badge),
and the `#blazor-error-ui` bar which is themed with the warning-subtle tokens.

---

## 9. Known design ↔ model gaps

1. **“Last crawl” on the Cinemas screen** has no model field. Derive it as the newest
   `PerformanceSnapshot.CrawledAt` for performances of the cinema, or add `Cinema.LastCrawlAt`. Design expects a
   short string plus the literal `Never`.
2. **Prices** are per price area (`PerformancePriceArea.OrderPrice`), not per performance. The card shows one
   value — implement as the minimum `OrderPrice` for the performance; show nothing if no areas were crawled.
3. **Posters**: `Film.PosterUrl` is nullable; the design's fallback is the centered `bi-film` tile on
   `--bs-secondary-bg`.
4. **`FavoriteSeatMatrix.Name` is required** in the model; the earlier design draft marked it optional. The
   editor now treats it as required — validate accordingly.
5. **Cinemas screen has no room-create action** — rooms come only from the widget-config seeding
   (`Re-seed rooms`); do not add a manual “Add room” button.
6. **Prototype `Cinema`/`Room` are strings**, matched by name in `data.js`. In the app they are ids
   (`Room.CinemaId`, `Performance.CinemaId`/`RoomId`); the “Cinema” select in the matrix editor is a *filter* for the
   Room list and is not persisted on `FavoriteSeatMatrix`.
7. **Multi-user is out of scope**: `FavoriteTimeWindow`, `FavoriteSeatMatrix`, `TrackedMovie` carry no
   `UserId`, and the design shows a single-user admin app (one `User`, setup-token flow).
8. **`NotificationLog` is only surfaced as “Recent Activity”** — there is no notification settings screen in
   the design. `NotificationType`/`Channel` are not exposed yet.

---

## 10. Implementation order

1. Tokens + layout shell (navbar, 1536px container, dark-mode block, `.cs-*` utilities).
2. `SeatGrid` and `FilmPerformanceCard` — every other screen consumes them.
3. Cinemas (pure CRUD over `Cinema`/`Room`, no derived data) — the cheapest end-to-end vertical slice.
4. Seat Matrices (filter bar, both tabs, precedence hint), Time Preferences.
5. Schedule / Tracked Movies (read models over `Performance`, `Film`, `TrackedMovie`).
6. Home + Performance Detail (need `Match`, `SeatStatus`, `PerformancePriceArea`).
7. Login / Setup.
