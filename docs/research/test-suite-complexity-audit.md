# Test-suite complexity audit (v1 spec yardstick)

Research for [#117](https://github.com/peterderkoala/cinescout/issues/117), part of map
[#114](https://github.com/peterderkoala/cinescout/issues/114). Lens: ponytail over-engineering
(delete / simplify / replace with stdlib, native or an already-installed dependency). Yardstick: the v1
spec, [#14](https://github.com/peterderkoala/cinescout/issues/14) (especially its **Testing Decisions**),
plus `docs/design/Technical Design Spec.md` (TDS). Measured on `dev` @ `e150686`. No code changed.

## Method and sources

- LOC: `wc -l` over `src/*.Tests/**/*.cs` (bin/obj excluded). Test count: `[Fact]`/`[Theory]` attributes.
  Executed cases come from `dotnet test` output.
- Runtime: `dotnet test src/cinescout.slnx --no-build` on the local dev box (Docker reachable), plus
  two single-class runs used as a controlled comparison.
- CRG (code-review-graph, built at `e150686`): `callers_of`, `tests_for` and `dead_code`. The
  `dead_code` result (360 symbols) flags every test method, because nothing calls a test, so it was
  not used as evidence.
- Spec text: `gh issue view 14`, plus a grep of TDS. The TDS has no section on testing, coverage or
  CRAP; the only testing guidance anywhere in the spec is #14's *Testing Decisions*.

## Baseline numbers

| Project | LOC | Test methods | Executed | Wall time (full-suite run) |
|---|---:|---:|---:|---:|
| `cinescout.core.Tests` | 4 220 | 120 | 123 | **4 m 28 s** |
| `cinescout.web.Tests` | 3 284 | 96 | 96 | 2 m 20 s |
| `cinescout.web.Client.Tests` | 1 252 | 50 | 60 | 0.3 s |
| `cinescout.contracts.Tests` | 304 | 20 | 36 | 0.2 s |
| `cinescout.persistence.Tests` | 242 | 1 | 1 | 28 s |
| **Total** | **9 302** | **287** (41 files) | **316** | **~4 m 34 s** (projects run in parallel) |

Production is 8 064 LOC of `.cs`/`.razor` without migrations, or 9 939 LOC with the 1 875 lines of
migrations. The ratio of test LOC to production LOC is about 1.15 : 1 without migrations.

Controlled comparison, both classes run on their own:

- `CinemaServiceTests` (core) uses one container per **test**: 11 tests take **26 s**.
- `ScheduleEndpointsTests` (web) uses one container per **class** and resets the data between
  tests: 10 tests take **3 s**.
- `TimeWindowMatcherTests` + `SeatBlockFinderTests` (pure logic): 17 tests take **46 ms**.

---

## List 1: Over-built (verdict, rationale, effort)

Ranked by payoff.

### O1. A fresh Postgres container and full migration for every test: SIMPLIFY (effort S)

- **Files:** 11 classes start a `PostgreSqlBuilder("postgres:18")` container in their instance
  `IAsyncLifetime.InitializeAsync` and run `MigrateAsync()` there. xUnit creates a new class instance
  for each test, so each test gets its own container. The classes are:
  - `core.Tests`: `CinemaServiceTests` (11 tests), `PreferenceServiceTests` (10),
    `KinoheldSeatCrawlServiceTests` (15), `HallOfFameCrawlServiceTests` (13),
    `MatchEvaluationServiceTests` (8), `SeatAvailabilityQueryTests` (8),
    `KinoheldRoomSeedingServiceTests` (8), `TrackedMovieServiceTests` (5), `HallOfFameCrawlJobTests` (1)
  - `persistence.Tests/CineScoutDbContextTests.cs` (1)
  - `web.Tests/LegacyPasswordHashMigratorTests.cs` (3)
- **Metric:** at least **83 containers per run**, each with a full migration. `core.Tests` accounts for
  4 m 28 s of the 4 m 34 s wall time. In the controlled comparison the per-test pattern takes
  2.4 s/test, against 0.3 s/test for `web.Tests`' per-class pattern.
- **Spec trace:** #14 says "`CineScoutDbContext` against a real, ephemeral Postgres via
  Testcontainers". The requirement is a real Postgres. Nothing in the spec asks for one container per
  test.
- **Fix:** reuse the pattern `web.Tests` already has (`IClassFixture` with one container and one
  migration per class, plus a `ResetDatabaseAsync` between tests, e.g. `ScheduleEndpointsTests.cs:281`).
  A single raw `TRUNCATE … RESTART IDENTITY CASCADE` resets the data, so no new dependency such as
  Respawn is needed. This takes containers from about 83 to 11, keeps classes running in parallel, and
  deletes roughly 20 lines of duplicated container code per class. Expected effect, extrapolated from
  the comparison rather than measured: `core.Tests` drops from minutes to tens of seconds.

### O2. Reflection-based client tests written to pass the CRAP gate: CUT and replace (effort S)

- **Files:**
  - `web.Client.Tests/SeatMatricesSaveAsyncTests.cs`: 401 LOC, 12 tests, 120 reflection hits
  - `TimePreferencesSaveAsyncTests.cs`: 279 LOC, 9 tests, 63 hits
  - `CinemasConfirmRenameAsyncTests.cs`: 248 LOC, 6 tests, 10 hits
- **Metric:** 928 LOC and 27 tests, about 10 % of the whole suite. A "hit" is a `SetField`/
  `GetField`/`BindingFlags` reference counted by grep. Each file's doc comment says it exists for
  "CRAP-score reduction" (#109/#110/#111, map #105).
- **Spec trace:** none. These tests contradict #14's own rule: "Good tests here exercise **external
  behavior** … not internal implementation details of how a class is structured internally". They set
  private Razor code-behind fields by name (`"_editorRoomId"` and similar), so renaming a field breaks
  a test even when behaviour is unchanged.
- **Fix:**
  - The validation branches (reversed rows, invalid party size, no days selected, end time not after
    start time) are really rules in the pure, shared `cinescout.contracts`
    `SeatMatrixValidation.Validate`, `TimeWindowValidation.Validate` and `CinemaValidation.Validate`.
    Those functions have **no direct tests today**. Replace the reflection tests with plain
    input/output tests of those three functions, about 100 LOC.
  - The happy path and the ProblemDetails mapping are already exercised end to end by
    `SeatMatricesEndpointsTests`, `TimePreferencesEndpointsTests` and `CinemasEndpointsTests`.
  - Delete the rest of the three files.

### O3. CRAP gate and coverage pipeline: CUT the gate; restore a plain CI (effort XS)

- **Files:**
  - `crap-check.sh` (69 LOC)
  - `coverage.sh` (38 LOC)
  - the `crap4dotnet` 0.1.1 and `reportgenerator` entries in `.config/dotnet-tools.json`
  - the CI steps plus the GitHub Pages coverage-badge job (last present in `.github/workflows/ci.yml`
    at `22fda6d`)
- **Metric:**
  - **The gate is not enforced today.** Commit `5e7134f` ("added crg") deleted both
    `.github/workflows/ci.yml` and `release.yml`. `dev` has no `.github/` directory at all, and `main`
    holds only the initial commit. So CI is missing entirely, not just the CRAP step. This looks
    accidental and should be confirmed with the owner.
  - The gate has caught three methods in its lifetime, all in `cinescout.web.Client` (#112). They were
    fixed by writing the O2 tests.
  - It needs an extra step to merge coverage, because `dotnet-crap` drops per-class coverage when given
    several Cobertura files. It also needs `rollForward`, because the tool targets .NET 8.
- **Spec trace:** none. #14 lists "CI/CD pipeline design" under **Out of Scope**, and neither #14 nor
  the TDS mentions coverage or CRAP.
- **Verdict:** cut `crap-check.sh` and the `crap4dotnet` tool. Bring back the minimal CI that
  `CLAUDE.md` still describes ("`ci.yml` runs `./run-tests.sh`"). Keep `coverage.sh` as a local,
  opt-in tool or drop it. The Pages badge job is cut as well, since no spec line asks for it.

### O4. Email tests for a capability with no consumer: CUT together with the code (effort XS)

- **Files:**
  - `core.Tests/EmailSenderTests.cs` (51 LOC)
  - `EmailSenderResilienceTests.cs` (57 LOC, starts a raw `TcpListener` to force retries)
  - `EmailSenderSmtpTests.cs` (72 LOC, a second Testcontainers image, `rnwood/smtp4dev:3.15.0`)
- **Metric:**
  - 180 LOC, 4 tests, plus one extra container image to pull in CI.
  - CRG `callers_of EmailSender.SendAsync` returns only these tests.
  - grep finds no production use of `IEmailSender` except its DI registration
    (`CoreServiceCollectionExtensions.cs:66`).
- **Spec trace:** none. #14 delivers notifications through a "plain **outgoing Discord webhook**"
  only, and the TDS and ADRs never mention email. The code comes from #42 (parent #37), which says
  "No downstream consumer … is built here".
- **Verdict:** cut, as part of whatever the domain/infra ticket of #114 decides for `Email/`. If
  `Email/` survives, keep only `EmailSenderTests` (no I/O).

### O5. The same rule tested at service level and again at endpoint level: SIMPLIFY (effort S)

- **Files:**
  - `core.Tests/PreferenceServiceTests.cs` (390 LOC, 10 tests) against
    `web.Tests/SeatMatricesEndpointsTests.cs` (387 LOC, 15 tests) and
    `TimePreferencesEndpointsTests.cs` (255 LOC, 10 tests)
  - `core.Tests/CinemaServiceTests.cs` (242 LOC, 11 tests) against
    `web.Tests/CinemasEndpointsTests.cs` (403 LOC, 16 tests)
- **Metric:** test names match pairwise:
  - "Only one general matrix per room" is tested **4 times**:
    `CreateSeatMatrix_SecondGeneralMatrixForSameRoom_IsRejected`,
    `UpdateSeatMatrix_IntoASecondGeneralMatrixForSameRoom_IsRejected`,
    `Post_SecondGeneralMatrixForSameRoom_ReturnsProblemDetails409` and
    `Put_IntoSecondGeneralMatrixForSameRoom_ReturnsProblemDetails409`.
  - Duplicate `ExternalCinemaId` on create and on update, delete blocked by rooms, delete without
    rooms, update, and rename room each have a twin at both layers. That is about 7 of the 11
    `CinemaServiceTests`.
  - The service-level copies are also the slow ones (O1).
- **Spec trace:** #14 Testing Decisions: test external behaviour. The endpoint test *is* the external
  behaviour, and the service test re-proves it from the inside.
- **Fix:** keep the endpoint tests. Trim `PreferenceServiceTests` and `CinemaServiceTests` to the cases
  the endpoints cannot reach, such as "missing id is a defensive no-op", or cut them if nothing is
  left. That removes roughly 400–600 LOC.

### O6. Ten copy-pasted `WebApplicationFactory` subclasses: SIMPLIFY (effort S)

- **Files:** every file in `web.Tests/*EndpointsTests.cs`, plus `LoginTests`, `SetupTests` and
  `ApiHardeningTests`.
- **Metric:**
  - 10 factory classes, each with the same container, migration, `ConfigureWebHost` and
    `UseEnvironment("Testing")` code.
  - `CreateAuthenticatedClientAsync` is copied 8 times, and the
    `"correct-horse-battery-staple"` password constant appears 10 times.
  - The factory-and-seed tails run 46–83 LOC per file, about 600 LOC in total.
- **Spec trace:** the duplication itself traces to nothing. The factories (a real host with real auth)
  trace to #14's "Auth … tested against the real hasher/middleware".
- **Fix:** one shared `CineScoutWebFactory` base class (container, migration, authenticated client,
  reset), with subclasses only where a test swaps a service (`FakeKinoheldClient`, the breaker). That
  removes about 250–350 LOC. Combining this with O1 is natural.

### O7. A test of a record's property getters: CUT (effort XS)

- **File:** `contracts.Tests/FilmPerformanceCardModelTests.cs` (66 LOC, 2 tests).
- **Metric:** both tests assign properties and assert that the getters return them. The file's own
  comment says "No behaviour to unit test on a plain data shape". The `required` modifier already
  enforces the required/optional split at compile time.
- **Spec trace:** TDS §4.1 (l.92) defines the prop table. The compiler enforces it; this test does not
  add anything.

### O8. A test of Mapperly-generated 1:1 code: CUT (effort XS)

- **File:** `contracts.Tests/Mapping/RoomMapperTests.cs` (52 LOC, 2 tests).
- **Metric:**
  - It tests generated code.
  - It is the **only** reason `cinescout.contracts.Tests` references `cinescout.web`. `coverage.sh`'s
    header comment explains that this cross-reference makes per-project coverage misleading.
  - `RoomDto` output is already asserted end to end by
    `CinemasEndpointsTests.GetDetail_IncludesRoomsOrderedByName`.
  - The sibling `SeatMatrixMapper` has no such test, which shows the pattern was never needed.
- **Spec trace:** #14 says to use "Mapperly … wherever DTO↔entity mapping is needed". It does not ask
  for tests of the generated code.
- **Replacement, if a guard is wanted:** raise Mapperly's unmapped-member diagnostics (RMG012/RMG020)
  to error level in `.editorconfig`. That uses an already-installed dependency and needs zero test
  LOC.

### O9. Tests for the legacy password-hash migrator: CUT together with the code (effort XS)

- **File:** `web.Tests/LegacyPasswordHashMigratorTests.cs` (89 LOC, 3 tests, 3 containers through O1's
  pattern).
- **Spec trace:**
  - #14 originally stored the hash in an env var. ADR 0001 replaced that with the `User` table.
  - The migrator only exists to carry an old `Auth__PasswordHash` across that change, for a
    single-operator app.
  - The release to a real VPS is still frozen (#100), so the number of deployments that need this
    upgrade path is probably zero.
- **Verdict:** cut, with the migrator. This is conditional: the owner first confirms that no running
  deployment still depends on the env var.

### O10. A whole test project for one test: SIMPLIFY (effort XS)

- **File:** `persistence.Tests` has 1 csproj and 1 test (`RoundTrips_one_instance_of_every_entity_type`,
  242 LOC). It takes 28 s, almost all of it container start-up.
- **Spec trace:** the test itself is spec-driven, since #14 says verify "EF Core mapping, migrations,
  and Npgsql-specific behavior (e.g. `jsonb`)". Giving it its own project is not.
- **Fix:** move the file into `core.Tests`, which already builds `CineScoutDbContext` against
  Testcontainers, and delete the project. It can then share O1's fixture.

### O11. Pin test for the duplicated weekday enum: KEEP, conditional (effort none)

- **File:** `contracts.Tests/WeekdayBitLayoutParityTests.cs` (40 LOC, 2 tests).
- **Rationale:** the test is cheap and correct, but it exists only because `cinescout.contracts` cannot
  see `cinescout.model`, which forces `Weekday` to be a copy of `DaysOfWeekFlags`. If the architecture
  ticket in #114 merges `contracts`, delete the test together with the copied enum. Otherwise keep it.

### Checked and found proportionate (no finding)

- **NSubstitute `Received()` assertions:**
  - 23 in total, spread over `MatchEvaluationServiceTests`, `KinoheldSeatCrawlServiceTests`,
    `HallOfFameCrawlServiceTests`, `TrackedMovieServiceTests` and `KinoheldRoomSeedingServiceTests`.
  - They assert *outward* effects the spec names: a notification was sent, with specific content; and
    polling stopped after the breaker tripped (US20). They do not inspect internal calls.
  - #14 asks tests to assert on "notification content/type selection".
- **Hand-rolled `HttpMessageHandler` stubs in `KinoheldClientTests`:** the HTTP edge is the agreed
  seam, and parsing the widget page's `dataLayer` is real logic.
- **`HallOfFameCrawlJobTests`:** 1 regression test that one failing cinema does not abort the whole
  cycle. It traces to US35.

---

## List 2: Spec-driven cost (no verdict)

This is complexity the spec itself demands. Whether any of it is worth reopening is for the #114
verdict grilling to decide.

| Cost | Where (LOC / tests) | Spec line it traces to |
|---|---|---|
| Docker is required to run the suite; at least about 11–13 Postgres containers per run even after O1 | all Postgres-backed classes (21 files) | #14 Testing Decisions: "`CineScoutDbContext` against a real, ephemeral Postgres via Testcontainers — deliberately *not* mocked" |
| Crawl upsert, cancellation after 2 unseen crawls, unconditional snapshots | `HallOfFameCrawlServiceTests.cs` 734 / 13 | #14 External integrations, Hall-of-Fame bullets; US1, US22, US23 |
| Kinoheld breaker (403/429/anomalous), 400/404 handled as expected outcomes, freshness window, 30 s force-refresh cooldown, `RoomId` resolution, honest User-Agent | `KinoheldSeatCrawlServiceTests.cs` 625 / 15, `KinoheldClientTests.cs` 333 / 16, `KinoheldRoomSeedingServiceTests.cs` 329 / 8 | #14 Kinoheld bullets; US12, US18–US21 |
| Matching engine (contiguous blocks, time windows, override precedence, `SeatAvailabilityChanged` only on a flip), query-time `HasOpenFavoriteMatrixSeats` | `MatchEvaluationServiceTests` 423 / 8, `SeatAvailabilityQueryTests` 315 / 8, `SeatBlockFinderTests` 152 / 7, `TimeWindowMatcherTests` 130 / 9 | #14 Matching & notification logic; US10, US13, US16, US17; "matching/rules engine as pure logic" |
| Track/untrack notifications and `NotificationLog`; notifier failure still logged | `TrackedMovieServiceTests` 187 / 5, `DiscordNotifierTests` 82 / 5 | #14 notification types 1–2; US4, US5; `IDiscordNotifier` seam |
| Real-middleware auth, login, cookie | `LoginTests` 120 / 4, `ApiHardeningTests` 169 / 6, `SetupTests` 172 / 7 | #14 Auth and "Auth … tested against the real hasher/middleware"; US25, US26. `SetupTests` and the CSRF part of `ApiHardeningTests` trace to ADR 0001 and ADR 0004, not to #14 |
| One endpoint test file per screen (`/api` JSON for the WASM client) | `web.Tests/*EndpointsTests.cs`, about 2.7k LOC / 80 tests | #14 Architecture: "Hosted Blazor WASM"; US30–US32; TDS §5 screens |
| Normative UI derivations in the client | `SeatGridCappingTests` 73 / 6 (TDS l.124, "Zone capping is normative"), `RelativeTimeFormatterTests` 53 / 6 (TDS l.306), `KinoheldStatusParserTests` 40 / 3 (TDS §5.7, l.245), `LiveSeatGridLayoutTests` 158 / 8 (TDS l.122 `live` grid with #14 `SeatStatus` neighbour IDs), `DayCodeConverterTests` 109 / 11 (TDS §6.3, l.285), `PerformanceDateTimeFormattingTests` 37 / 3 | TDS sections as cited |
| Round-trip of every entity, including `jsonb` | `CineScoutDbContextTests` 242 / 1 | #14 Testing Decisions (EF mapping, migrations, `jsonb`) |

## Rough total if every cut/simplify is accepted

- About 2.0–2.5k test LOC removed (O2 928, O4 180, O5 about 500, O6 about 300, O1 about 200,
  O7–O9 207). The suite goes from about 9.3k to about 7k LOC.
- Two tool dependencies (`crap4dotnet`, plus `reportgenerator` if coverage goes too) and one test
  project are removed.
- One container image (smtp4dev) is no longer needed.
- Wall time goes from about 4.5 min to well under 1 min. This is an extrapolation from the controlled
  comparison and was not measured.
- Separately from all of this, CI itself is currently missing from `dev` (O3).
