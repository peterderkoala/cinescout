# Cinemas screen: service seam, re-seed trigger, and delete semantics

`/cinemas` (`Technical Design Spec.md` §5.1) doesn't exist in code — no page, no route, no service. Unlike
the five screens converting from static SSR, this one is built fresh, directly in the WASM-behind-an-API
architecture [Render mode per screen](https://github.com/peterderkoala/cinescout/issues/74) already settled
for all seven authenticated screens: the "service seam" question here is what the new `CinemasEndpoints.cs`
(named in [HTTP API surface for the WASM client](https://github.com/peterderkoala/cinescout/issues/83)) calls
into, not what a Razor component injects.

## Decisions

**Service placement.** Every existing screen's write path already goes through a dedicated service
(`TrackedMovieService`, `PreferenceService`) while reads stay direct `CineScoutDbContext` queries; no page
does a raw write without either a service or a `Program.cs` minimal-API handler in front of it. A new
`CinemaService` in `cinescout.core/Cinemas/` follows that shape — constructor-scoped over
`CineScoutDbContext`, `AddScoped`, defensive no-ops on missing ids — for Create/Update/Delete.
`CinemasEndpoints.cs` queries `CineScoutDbContext` directly for GET/list/detail reads and maps to DTOs; there
is no read-side service, matching the existing write/read split.

**Re-seed Rooms.** `KinoheldRoomSeedingService.SeedRoomsForCinemaAsync` is cheap and bounded (one HTTP call
plus a small per-auditorium loop, no pagination) and already runs synchronously today, awaited at startup.
There is no fire-and-forget Hangfire precedent anywhere in the repo (`IBackgroundJobClient`/
`BackgroundJob.Enqueue` are unused; the only two Hangfire jobs are recurring). Re-seed Rooms runs
synchronously inside the request, matching Force Refresh — the closest existing analog. Breaker and cooldown
enforcement ([API authentication, authorization and CSRF defence](https://github.com/peterderkoala/cinescout/issues/85)
gave Re-seed Rooms its own cooldown tracker) move *into* `SeedRoomsForCinemaAsync` itself, symmetric with how
`KinoheldSeatCrawlService.FetchForPerformanceAsync` already checks `KinoheldCircuitBreaker` and its own
cooldown tracker internally rather than leaving it to the caller. The method returns a typed outcome (the
room-seeding analog of `SeatFetchOutcome`) that the endpoint translates to `ProblemDetails` plus
`kinoheldStatus`, the same contract shape ADR 0004 established for Force Refresh.

**Delete Cinema.** The EF cascade chain is fully configured at the DB level: deleting a `Cinema` cascades
through `Room` → `Performance` → `SeatStatus`/`Match`/`PerformancePriceArea`/`PerformanceSnapshot`/
`SeatingSnapshot`/`FavoriteSeatMatrix`, and `Match` cascades further to `NotificationLog`. A bare delete would
silently erase a cinema's entire crawl/match/notification history in one click. `CinemaService` blocks
deletion whenever the cinema has any `Room` rows — a `Room` existing means at least one successful seed
happened — and directs the operator to the existing `Cinema.IsActive` toggle ("pause crawling") instead. True
delete is reserved for cinemas added by mistake or never successfully seeded, where the cascade has nothing
of substance to destroy.

**Add-cinema validation.** Required-field and URL-format checks only, at save time, no live "test connection"
call — matching the hand-rolled `if`/early-return idiom every other form in the app already uses (never
attribute-based, never a live external check). The already-spec'd read-only `KinoheldCinemaId` field ("Not
resolved yet" until the first successful widget-config fetch) is the real feedback mechanism for whether a
cinema is configured correctly, surfacing after the fact rather than gating save.

**No manual "Add room."** Confirmed, not reopened: §9 item 5 of the design spec already rules this out —
rooms come only from Re-seed Rooms seeding. Nothing in the above smuggles a create-room path back in.

## Considered Options

- **Reads also routed through `CinemaService`** (rejected): no existing screen does this; the read/write split
  (raw `DbContext` for reads, service for writes) is already the established rule, not something this ticket
  needs to invent.
- **Re-seed Rooms enqueued to Hangfire** (rejected): would be the first fire-and-forget job in the codebase,
  needs a way to report the outcome back to a client that's still waiting for a response, and the underlying
  work is cheap enough that Force Refresh's synchronous precedent applies directly.
- **Breaker/cooldown checks in `CinemaService` or the endpoint rather than inside `KinoheldRoomSeedingService`**
  (rejected): would duplicate the pattern `KinoheldSeatCrawlService` already establishes for the same class of
  problem, splitting Kinoheld-call enforcement across two places instead of one.
- **Unconditional cascade delete** (rejected): the spec doesn't ask for a delete confirmation flow rich enough
  to make one-click history loss safe, and `IsActive` already exists as the non-destructive way to stop a
  cinema being crawled.
- **Soft-delete** (rejected): requires a schema change for a "pure CRUD" screen (§10) with no stated need for
  recovering a deleted cinema; block-if-non-empty gets the same safety without a new field.
- **Live "test connection" action on Add Cinema** (rejected): no precedent for external-call validation
  anywhere in the app, and the read-only `KinoheldCinemaId` field already surfaces whether seeding actually
  worked, just after the fact instead of at save time.

## Consequences

- New `cinescout.core/Cinemas/CinemaService.cs`, registered via an `AddCinemas` extension following the
  `AddTrackedMovies`/`AddPreferences` pattern.
- `KinoheldRoomSeedingService.SeedRoomsForCinemaAsync` changes signature from bare `Task` to a typed outcome
  (mirroring `SeatFetchOutcome`), and gains the breaker/cooldown checks `KinoheldSeatCrawlService.FetchForPerformanceAsync`
  already has.
- A new cooldown tracker sibling to `KinoheldFetchCooldownTracker`, scoped per-cinema (~5 minute window, per
  ADR 0004), is needed before Re-seed Rooms can be implemented.
- `CinemaService.DeleteAsync` needs a `Room` existence check before deleting, and the endpoint needs a
  `ProblemDetails` response shape for the blocked case.
- Building this screen also requires implementing [ADR 0003](https://github.com/peterderkoala/cinescout/blob/dev/docs/adr/0003-cinema-last-crawl-at-field.md)'s
  `Cinema.LastCrawlAt` field, which is designed but not yet in the model or a migration.
