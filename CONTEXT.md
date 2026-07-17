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
A record of one attempt to notify the user (e.g. via Discord) about a `Match`. Separate from `Match` so retries or additional channels don't change the match record itself.
_Avoid_: Alert log, notification
