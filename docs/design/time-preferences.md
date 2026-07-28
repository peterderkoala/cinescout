# Time Preferences page design

Resolves [wayfinder ticket #68](https://github.com/peterderkoala/cinescout/issues/68), part of the
[page-design reference set map](https://github.com/peterderkoala/cinescout/issues/61). See
[`00-design-system.md`](00-design-system.md) for color/type/spacing/breakpoints and
[`layout-navigation.md`](layout-navigation.md) for the `MainLayout` shell this page renders inside.

**Current state**: `TimePreferences.razor` is a single, **non-tabbed** page — the `?tab=` in-page
tab pattern mentioned generally in root `CLAUDE.md` turns out to be specific to Seat Matrices, not
this page. Structure: a `.list-group` of existing `FavoriteTimeWindow`s (bold days summary +
start–end time, Edit/Delete buttons per row), and one shared editor `.card` **below** the list for
both Add and Edit — 7 inline day-checkboxes plus native `<input type="time">` fields. Both list and
editor already use a hand-set `max-width: 32rem` narrow column inside the page's `.container-fluid`
shell — kept as-is; `layout-navigation.md` already anticipated exactly this kind of narrower column
for a form-heavy page like this one.

**Render mode**: static-SSR-only. Edit flow already follows this app's established pattern —
`?editId=` pre-populates the editor only on the initiating `GET`, so posted form values win on
`POST`; successful mutations redirect back to `/time-preferences` (POST-redirect-GET). This
mechanism is unchanged by this doc — only the visual design around it changes.

## Day-of-week selector: toggle buttons, not checkboxes

Replace the 7 inline checkbox+label pairs with a **segmented toggle-button group** — Bootstrap's
`.btn-check` + `.btn-outline-*` pattern, styled as 7 equal-width day-letter buttons (Mo/Tu/We/Th/
Fr/Sa/Su) in a single row, filling solid with the design system's burgundy primary when active.

Rationale: seven small checkbox+label pairs crammed inline are fiddly to tap precisely on a phone
(this app is checked from phones per the design system's mobile-first posture), and don't read as
one cohesive "pick your days" control. A row of toggle buttons is a well-known pattern (calendar/
alarm-clock apps), easier to tap, and reads as a single unit.

## Editor placement: above the list

The shared editor card (used for both "Add" and "Edit," switching on `EditingId`/`EditId`) moves
**above** the list of existing windows, not below. Once several time windows exist, the previous
below-the-list placement meant scrolling past all of them just to add another — moving it up keeps
both the common "add a window" action and the "edit this one" action (triggered via `?editId=`)
immediately visible without scrolling.

## List rows: day badges, not text

Each list row's days render as small filled pill badges (Mo/We/Fr, same burgundy primary as the
active toggle buttons) instead of the current bold comma-separated text summary. Keeps the visual
language consistent between "how you pick days" (the editor) and "how a saved window shows its
days" (the list), and scans faster than reading a text string per row.

## Kept as-is (no change from current implementation)

- Narrow `max-width` column for both list and editor, inside the page's `.container-fluid` shell.
- Native `<input type="time">` fields for start/end — no custom time-range slider needed for two
  plain time values.
- Error alert as `.alert.alert-danger` (consistent with `auth.md`'s established error treatment).
- Per-row button conventions: `Edit` as `.btn-outline-secondary`, `Delete` as `.btn-outline-danger`.

## Solution references

- [Bootstrap 5.3 — Checks & radios (toggle buttons)](https://getbootstrap.com/docs/5.3/forms/checks-radios/#toggle-buttons)
  — the `.btn-check` pattern for the day selector
- [Bootstrap 5.3 — Badge](https://getbootstrap.com/docs/5.3/components/badge/) — day badges in the
  list rows
- [Bootstrap 5.3 — List group](https://getbootstrap.com/docs/5.3/components/list-group/) (existing
  list structure, kept)
- [`00-design-system.md`](00-design-system.md) and [`layout-navigation.md`](layout-navigation.md)
  — inherited, not restated here
