# Domain & infrastructure complexity audit (vs. v1 spec)

Research ticket [#116](https://github.com/peterderkoala/cinescout/issues/116), part of wayfinder map
[#114](https://github.com/peterderkoala/cinescout/issues/114). Measured against `dev` @ `e150686`.

**Scope**: `src/cinescout.core` + the web host's service wiring (`Program.cs`,
`Extensions/WebServiceCollectionExtensions.cs`, `HealthChecks/`). Hangfire, `KinoheldCircuitBreaker`,
the cooldown trackers, the resilience pipelines (HTTP + hand-built SMTP), the Discord and email
notifiers, the matching services and single-implementation interfaces.

**Yardstick**: the v1 spec. That is [#14](https://github.com/peterderkoala/cinescout/issues/14) (cited as
"#14 §<heading>" or "#14 story N"), plus `docs/design/Technical Design Spec.md` (cited as "TDS:<line>").
ADRs are re-examined here, not treated as spec.

**Lens**: ponytail over-engineering. Can it be deleted, simplified, or replaced by stdlib, the platform or
an already-installed dependency?

## Method & metrics

- **LOC**: `wc -l` per file. `cinescout.core` is 2,316 prod LOC in 33 files, and `cinescout.core.Tests`
  is 4,220 LOC.
- **CRAP**: a fresh `./coverage.sh && ./crap-check.sh` run on this commit. All tests passed, and
  `crap-check.sh` reported **0 methods over CRAP 30** across all six projects. The highest in scope
  are `MatchEvaluationService.EvaluateAsync` (12.01, cc 12) and `TimeWindowMatcher.ToFlag` (11.08, cc 8).
  So no finding below rests on CRAP. Complexity here is *breadth* (extra types, extra knobs), not
  gnarly methods.
- **CRG**:
  - `refactor_tool mode=dead_code` on `cinescout.core/` returned 21 symbols. Most are false positives:
    DTO records and sealed-record union variants that are constructed or pattern-matched rather than
    "called". The one true positive kept is `KinoheldSeatCrawlJob` (see O4).
  - `find_large_functions_tool` (≥30 lines) returned 14 functions. The largest is
    `KinoheldSeatCrawlService.StoreAsync` at 88 lines.
  - `query_graph_tool inheritors_of IKinoheldClient` returned `KinoheldClient` plus a test fake.
  - Injection-site and consumer checks used `grep`, because the graph doesn't model DI registration
    or string config keys.

---

## List 1: Over-built (verdict + rationale + rough effort)

Effort: XS < 1h, S ≈ half a day, M ≈ 1–2 days.

### O1. `IEmailSender` / `EmailSender` + hand-built SMTP resilience pipeline: **CUT** (S)

- **Files**:
  - `src/cinescout.core/Email/EmailSender.cs` (80 LOC) and `Email/IEmailSender.cs` (13 LOC).
  - `CoreServiceCollectionExtensions.AddEmail` and the `.AddEmail()` call at `src/cinescout.web/Program.cs:59`.
  - Tests: `EmailSenderTests.cs` (51), `EmailSenderResilienceTests.cs` (57) and `EmailSenderSmtpTests.cs` (72),
    so 180 test LOC.
  - Packages: `MailKit` and `Microsoft.Extensions.Resilience` (the latter used only by `EmailSender`;
    `DiscordNotifier`'s `Polly.Timeout` comes transitively from `Http.Resilience`).
  - Config: 5 `Email__*` keys in `docker/.env.example:28-35`.
- **Metric**:
  - A consumer search for `IEmailSender` outside tests finds only the DI registration. Nothing injects it.
  - `EmailSender.SendAsync`: CRAP 6.03, cc 6, 43 lines.
- **Spec trace**: **none**.
  - #14 §"Matching & notification logic" says delivery is "a plain outgoing Discord webhook for this build".
  - #14 story 29 lists secrets as "database connection string, Discord webhook URL, auth credential", with no SMTP.
  - The originating ticket #42 (parent #37) itself says: *"No downstream consumer (password reset, 2FA) is built here."*
- **Rationale**: this is a speculative capability with zero callers, plus a second resilience idiom
  (`ResiliencePipelineBuilder`) that exists only to serve it. It's pure YAGNI. When a real email
  consumer arrives, it can scaffold for itself.
- **Effort (S)**: delete 2 prod files, 3 test files, `AddEmail`, 2 package refs, the `.env.example`
  block and the CLAUDE.md `Email/` paragraph.

### O2. Two copy-paste cooldown trackers: **SIMPLIFY** (S)

- **Files**: `Kinoheld/KinoheldFetchCooldownTracker.cs` (33 LOC) and `Kinoheld/KinoheldRoomSeedCooldownTracker.cs`
  (34 LOC). The bodies are identical (`Lock` + `Dictionary<int, DateTimeOffset>` + `TryBegin…`). Only the
  `TimeSpan` constant (30 s vs 5 min) and the names differ.
- **Spec trace**: split.
  - The 30 s force-refresh cooldown **traces**: #14 §"External integrations" says "30-second per-performance
    cooldown purely to prevent accidental double-click", and TDS:255 has the cooldown alert copy.
  - The 5-min re-seed cooldown traces only to ADR 0004:42 / ADR 0005:77, **not** the spec.
  - TDS:166 specifies the Re-seed rooms button but no cooldown.
- **Rationale**: one `KeyedCooldown(TimeSpan window)` class, registered as two instances (keyed services),
  removes about 30 LOC and one type. The cheaper alternative is to drop the re-seed cooldown outright:
  a single operator pressing a manual button is already guarded against Kinoheld push-back by the
  circuit breaker. That would reopen ADR 0005, so it's left to the verdict grilling.
- **Effort**: S. That covers the class merge plus re-pointing `KinoheldRoomSeedingServiceTests` /
  `KinoheldSeatCrawlServiceTests`.

### O3. `KinoheldCircuitBreaker`: three lock-guarded getters for a one-way latch: **SIMPLIFY** (XS)

- **File**: `Kinoheld/KinoheldCircuitBreaker.cs` (65 LOC). It has a `Lock` plus three properties
  (`IsTripped`, `TripReason`, `TrippedAt`), each with its own `lock` block, and a `Trip()` that is
  first-write-wins. `Trip`: CRAP 2.03.
- **Spec trace**: the **behaviour traces**. #14 §"External integrations" says it "stops all Kinoheld
  polling entirely … until manually reset", and #14 story 20 backs that. The *implementation*
  deviates from the spec's wording ("A Polly circuit breaker"). It is hand-rolled.
- **Rationale**: a single immutable `record Trip(string Reason, DateTimeOffset At)` in one field, set via
  `Interlocked.CompareExchange`, gives the same first-trip-wins latch in about 15 LOC with no lock.
  Polly's `CircuitBreakerManualControl.IsolateAsync()` would match the spec text literally but adds
  ceremony, and the plain latch is the lazier fit. Keep the behaviour; shrink the type.
- **Spec conflict to surface (not an over-build)**: TDS:252's normative copy reads *"…circuit breaker is
  open. Retrying automatically."* The code never retries; the breaker stays tripped until an app restart
  (`KinoheldSeatCrawlService.cs:152,160`). Either the copy or the breaker semantics is wrong. Hand this to
  the verdict ticket.

### O4. `KinoheldSeatCrawlJob`: pure pass-through wrapper: **CUT** (XS)

- **File**: `Kinoheld/KinoheldSeatCrawlJob.cs` (13 LOC). Its body is
  `await crawlService.CrawlTrackedAsync(ct)`, plus a DI registration at
  `CoreServiceCollectionExtensions.cs:51`.
- **Metric**: CRG `dead_code` flags it, because it's referenced only through Hangfire's generic
  `AddOrUpdate<KinoheldSeatCrawlJob>`.
- **Spec trace**: #14 §"Architecture & hosting" requires Hangfire-triggered crawlers. It doesn't require
  a job class per crawler.
- **Rationale**: `recurringJobManager.AddOrUpdate<KinoheldSeatCrawlService>("kinoheld-seat-crawl",
  s => s.CrawlTrackedAsync(CancellationToken.None), …)` does the same thing. **Keep `HallOfFameCrawlJob`**:
  it has real logic (an active-cinema loop and per-cinema try/catch from ADR 0003), so it isn't a
  wrapper.

### O5. `SeatAvailabilityQuery`'s single-performance overload: production-dead: **SIMPLIFY** (S)

- **File**: `Matching/SeatAvailabilityQuery.cs:16-32` (17 of 72 LOC).
- **Metric**:
  - All three production callers use the *batch* overload: `HomeEndpoints.cs:174`,
    `ScheduleEndpoints.cs:87` and `TrackedMoviesEndpoints.cs:95`.
  - `PerformanceDetailEndpoints.cs:140` (the one single-performance page) bypasses the class and calls
    `SeatMatrixResolver` + `SeatBlockFinder` inline.
  - Despite that, 6 of the 10 tests in `SeatAvailabilityQueryTests.cs` (315 LOC) target the dead overload.
  - CRAP caveat: the run measured **0 % coverage for both overloads** (batch: CRAP 30.0, exactly at the
    gate; single: 12). That is most likely an async-state-machine attribution artefact, since the tests
    do call them, but it's worth a look because the production-used method sits *on* the CRAP ceiling.
- **Spec trace**: #14 §"Matching & notification logic" names `HasOpenFavoriteMatrixSeats(performanceId)`
  as a query-time *capability*. The batch form delivers that capability, so a second entry point
  doesn't trace.
- **Rationale**: drop the overload, retarget the 6 tests at the batch overload with a one-element list,
  and optionally have `PerformanceDetailEndpoints` use it too, so the matrix/block logic has one caller
  shape.

### O6. Two config knobs for one spec value: freshness window vs crawl interval: **SIMPLIFY** (XS)

- **Files**:
  - `KinoheldSeatCrawlService.cs:89` reads `Kinoheld:SeatFreshnessMinutes` (default 30).
  - `WebServiceCollectionExtensions.cs:145` reads `Kinoheld:SeatCrawlIntervalMinutes` (default 30).
  - Both are documented separately in `docker/.env.example:44,49`.
- **Spec trace**: #14 §"External integrations" says the on-demand fetch is skipped "if data … is already
  within **the same** 30-minute freshness window as the recurring crawl". That's one value, not two.
- **Rationale**: two independent knobs can drift. For example, a 60-min crawl with a 30-min freshness
  window makes browsing re-fetch data the crawl considers current. Read the single crawl-interval key
  in both places and drop `SeatFreshnessMinutes`. This is a config knob for a value the spec ties to
  another one.

### O7. `NewFilmAdded` notification: outside the v1 spec: **KEEP** (n/a)

- **Files**: `HallOfFame/HallOfFameCrawlService.cs:79,90-92` (about 5 LOC), the `NotificationType.NewFilmAdded`
  enum member, and the corresponding `HallOfFameCrawlServiceTests` cases.
- **Spec trace**: **none in v1**. #14 §"Matching & notification logic" lists exactly "Four notification
  types". This one came later, from owner-requested map #43 / ticket #44.
- **Rationale**: it's recorded so the verdict ticket knows it's off-spec. It's tiny, it reuses
  `NotificationDispatcher`, and it was a deliberate post-v1 ask, so there's nothing to win by cutting it.

---

## List 2: Spec-driven cost (no verdict)

This complexity the spec itself demands. Reopening any of it means reopening the spec.

### S1. Hangfire + Hangfire.PostgreSql: plus an unwired dashboard

- **Spec**:
  - #14 §"Architecture & hosting": "Scheduling: **Hangfire** + **Hangfire.PostgreSql** (dashboard +
    persisted job history in Postgres)".
  - #14 story 33: "a background job scheduler with visibility into job history".
- **Cost**:
  - 2 packages, plus a third pinned only to lift a vulnerable transitive floor (`cinescout.web.csproj:13`).
  - `AddHangfireInfrastructure` and `ScheduleRecurringJobs` (`WebServiceCollectionExtensions.cs:100-106,132-150`),
    including the `IRecurringJobManager`-vs-static-facade gotcha documented in CLAUDE.md.
  - `HangfireHeartbeatHealthCheck.cs` (32 LOC).
  - A `"Testing"`-environment branch that skips Hangfire.
  - The Hangfire schema in Postgres.
- **Observation for the verdict grilling**:
  - **No `UseHangfireDashboard` / `MapHangfireDashboard` exists anywhere in `src/`** (grep, 0 hits).
    Story 33's stated reason for picking Hangfire is the dashboard, and that reason isn't delivered.
  - Today Hangfire runs exactly two fixed-interval crons. A `BackgroundService` + `PeriodicTimer`
    (stdlib) would cover that.
  - So the spec pays for Hangfire without receiving the dashboard. There are two ways out:
    - map the dashboard behind the existing cookie-auth fallback policy (~3 LOC) and keep everything; or
    - reopen story 33 and replace Hangfire with `PeriodicTimer`, which removes 3 packages, the
      health check and the Testing-env branch.

### S2. `AddStandardResilienceHandler()` on all three HTTP clients + Kinoheld retry override

- **Spec**:
  - #14 §"Architecture & hosting": "`AddStandardResilienceHandler()` for every outbound HTTP client
    (Hall-of-Fame, Kinoheld, Discord webhook)".
  - #14 story 35.
  - #14 story 20 (don't worsen a block).
- **Cost**: `CoreServiceCollectionExtensions.cs:19-45`. The Kinoheld `Retry.ShouldHandle` override
  (13 lines) is what stops the standard pipeline from retrying 403/429 into the block, which is
  required for story 20. `DiscordNotifier`'s catch list includes `TimeoutRejectedException` only because
  the handler can throw it.

### S3. Kinoheld crawl-posture machinery

- **Spec**:
  - #14 §"External integrations": the breaker trips on 403/429/anomalous; 400/404 are expected and
    skipped; the freshness window; force-refresh with a 30 s cooldown; an honest UA.
  - #14 stories 18–21.
  - TDS:250-255, the four-row outcome-alert table.
- **Cost**:
  - `KinoheldSeatsResult.cs` (66 LOC, sealed union) and `KinoheldWidgetConfigResult.cs` (22).
  - `SeatFetchOutcome.cs` (26, which maps 1:1 onto the TDS §5.7 alert table) and `RoomSeedOutcome.cs` (17).
  - The breaker gate at 3 call sites (`KinoheldSeatCrawlService.cs:33,73`, `KinoheldRoomSeedingService.cs:33`).
  - The force-refresh cooldown (the spec half of O2).
  - `KinoheldSeatCrawlService.cs` as a whole: 307 LOC, `StoreAsync` 88 lines (the largest function in core,
    CRAP 8). It covers the spec's `SeatStatus` current-state mirror, the `SeatingSnapshot` history, the
    first-crawl `RoomId` resolution, and price areas for story 15's price.

### S4. Eager room seeding via the widget page's inline `dataLayer`

- **Spec**: #14 §"External integrations" says "`Room` seeding is eager, not lazy: fetched once per `Site`
  from Kinoheld's widget config". TDS:166 specifies the Re-seed rooms action.
- **Cost**:
  - `KinoheldRoomSeedingService.cs` (110 LOC).
  - `KinoheldClient.ParseDataLayer` (40 lines, CRAP 7.02): a `Utf8JsonReader` token-match, because the
    blob embeds SVG with braces.
  - `KinoheldClient.cs` is 250 LOC overall.

### S5. Three single-implementation interfaces at the HTTP edge

- `IHallOfFameClient` (10 LOC), `IKinoheldClient` (56 LOC incl. DTO records) and `IDiscordNotifier` (12 LOC).
  Each has exactly one production implementer. CRG `inheritors_of IKinoheldClient` returns only
  `KinoheldClient` plus `FakeKinoheldClient` in `cinescout.web.Tests`.
- **Spec**: #14 §"Testing Decisions" names exactly these three as the agreed NSubstitute seams.
  ("No interface with one implementation" is the ponytail default; the spec overrides it deliberately.)
  `IEmailSender` is the only single-impl interface in scope that *doesn't* trace (see O1).

### S6. Matching engine

- **Spec**:
  - #14 §"Matching & notification logic": time window ∧ contiguous block ≥ `PartySize`, film-override
    precedence, a Match created once per pair, `SeatAvailabilityChanged` only on a flip, and
    `RulesMatched` content including poster and price.
  - #14 §"Testing Decisions": pure logic, no seam.
- **Cost**:
  - `SeatBlockFinder.cs` (62), `TimeWindowMatcher.cs` (59) and `SeatMatrixResolver.cs` (43).
  - `MatchEvaluationService.cs` (168). Its `EvaluateAsync` is the most complex method in scope
    (CRAP 12.01, cc 12) and still well under the gate.
  - `CinemaTimeZone.cs` (16). It's needed because `FavoriteTimeWindow` is Berlin wall-clock while
    `StartsAt` is an instant.
  - `FindCheapestMatchingPriceAsync` plus the `PerformancePriceArea` upsert in S3, both for story 15's price.
- **Deviation (not over-build)**: the spec says a non-qualifying `Match` "flips to `Superseded`". The code
  instead keeps it `Active` and toggles `HasSufficientSeats`. That's simpler and still delivers
  story 16; flagged only so the verdict ticket can reconcile the spec text.

### S7. Notify-and-log on every send (`NotificationDispatcher`, `DiscordNotifier` never-throw)

- **Spec**: #14 §"Data model" `NotificationLog` (`Channel`, `Status`, `ResponseDetail`), and #14 §"Testing
  Decisions", which asserts "the resulting `NotificationLog` entries".
- **Cost**:
  - `Discord/NotificationDispatcher.cs` (39 LOC). Its 3 callers are `MatchEvaluationService`,
    `TrackedMovieService` and `HallOfFameCrawlService`.
  - `DiscordNotifier.cs` (36 LOC, CRAP 3.01).
  - Config read at call time, so an unset webhook is a logged failure, not a crash.

---

## Summary for the verdict ticket

| # | Item | Verdict | Effort | Spec trace |
|---|------|---------|--------|------------|
| O1 | Email sender + SMTP pipeline | cut | S | none (#14: Discord-only; #42: "no downstream consumer") |
| O2 | Two identical cooldown trackers | simplify (merge; optionally drop re-seed cooldown) | S | 30 s yes; 5 min ADR-only |
| O3 | Circuit-breaker class shape | simplify (latch in one field) | XS | behaviour yes, impl not Polly; TDS:252 copy conflicts |
| O4 | `KinoheldSeatCrawlJob` wrapper | cut | XS | no |
| O5 | Single-performance `SeatAvailabilityQuery` overload | simplify (cut overload) | S | capability yes, 2nd entry point no |
| O6 | `SeatFreshnessMinutes` knob | simplify (reuse crawl interval) | XS | spec says "same" window |
| O7 | `NewFilmAdded` | keep | – | post-v1 (#43/#44) |
| S1 | Hangfire, **dashboard unwired** | spec-driven, decide: wire dashboard or reopen story 33 | – | #14 story 33 |
| S2–S7 | Resilience, crawl posture, room seeding, 3 seams, matching, notify-and-log | spec-driven | – | see above |

Net cut/simplify potential in scope is roughly 93 prod + 180 test LOC (O1), plus about 30 (O2), about
50 (O3), 13 (O4) and 17 prod + test retargeting (O5). That's about 200 prod LOC (~9 % of
`cinescout.core`) and 2 packages, with no spec behaviour lost. The bigger lever is S1, and it's a
spec question, not a code one.
