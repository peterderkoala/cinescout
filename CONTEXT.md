# CineScout

CineScout tracks movie screenings at Hall of Fame Kamp-Lintfort (and, in future, other sites), crawls seating availability, and alerts the user when a watched film's screening matches their preferences.

## Language

**Site**:
One physical cinema location CineScout crawls (e.g. "HALL OF FAME - Kino in Kamp-Lintfort"). CineScout is multi-site-ready even though only one is configured today.
_Avoid_: Cinema, theater, location

**Film**:
A film listing as it appears at one specific `Site`, matching the source API's own scoping (`detailId` + `filmSite`) — not a cross-site canonical movie identity. The same movie playing at two sites is two `Film` rows.
_Avoid_: Movie (as an entity name — fine as prose)

**Performance**:
A single bookable showing of a `Film` — one film, one date/time, one room. Matches the source Hall-of-Fame API's own vocabulary (`performanceID`) rather than introducing a synonym.
_Avoid_: Screening, showing, showtime

**Room**:
A named physical auditorium at a `Site` (e.g. "Kino 1"–"Kino 7" at Kamp-Lintfort), sourced from Kinoheld's widget config rather than the Hall-of-Fame schedule API, which has no room concept at all.
_Avoid_: Auditorium (as the entity name — Kinoheld's own vocabulary, kept as a synonym note only), Saal, screen

**WatchedMovie**:
A `Film` the user has marked as interesting. Considered active only while the film still has upcoming performances — determined at query time, not stored as a flag.
_Avoid_: Watchlist item, favorite film

**FavoriteTimeWindow**:
A user-defined day-of-week + time range the user prefers performances to fall within. Multiple windows can be active at once; a performance matches if it falls in any of them.
_Avoid_: Favorite time, preferred slot

**Match**:
A record that one `Performance` has satisfied a `WatchedMovie`'s active preferences at a point in time. Distinct from whether a notification was actually sent for it (see `NotificationLog`).
_Avoid_: Alert (that's the outward-facing behavior a `Match` triggers, not the record itself)

**NotificationLog**:
A record of one attempt to notify the user (e.g. via Discord). Carries a `NotificationType` (`WatchStarted`, `WatchStopped`, `RulesMatched`, `SeatAvailabilityChanged`); `MatchId` is only set for the latter two, since watch-lifecycle notifications aren't tied to a specific `Match`.
_Avoid_: Alert log, notification

**SeatStatus**:
The current free/sold state of one seat (row + seat number) for one `Performance`, upserted each Kinoheld crawl. Carries adjacency (`LeftNeighborSeatId`/`RightNeighborSeatId`) so contiguous free blocks can be located.
_Avoid_: Seat, seat map entry

**FavoriteSeatMatrix**:
A user-defined zone within a `Room` — a row range plus a seat-number range plus a `PartySize` — used to check whether enough adjacent seats are free for the user's group. Optionally scoped to one `Film` (a specific override), otherwise general for that `Room`; a film-specific matrix takes precedence over a general one. Also subsumes "liking a room in general" (a wide-open matrix), so there is no separate `FavoriteRoom` concept. Supersedes the earlier, narrower "RoomRule" idea.
_Avoid_: RoomRule, FavoriteRoom, seat rule, priority matrix

**User**:
An operator account that can log in to CineScout. Replaces the earlier hardcoded single-credential model (a config-only password hash with no persisted identity). The one row is seeded ahead of first use rather than created by the setup flow itself — see [ADR 0001](docs/adr/0001-user-table-seeded-with-null-password-hash.md) — and the schema is deliberately minimal (no roles/permissions) but shaped so a future second `User` doesn't need a breaking migration.
_Avoid_: AppUser, Account
