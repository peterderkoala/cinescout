# Architecture complexity audit against the v1 spec

Research for [#115](https://github.com/peterderkoala/cinescout/issues/115), part of map
[#114](https://github.com/peterderkoala/cinescout/issues/114). Scope is layer 1 only: project count, the
`contracts` split, the hosted WASM + static-SSR hybrid with its `/api` and antiforgery plumbing, and Mapperly.
Domain/infra code is #116, tests are #117, and docs are #118. Findings from those layers are only passed along
(see the last section).

- **Measured at:** `dev` @ `e150686`.
- **Yardstick:** [#14](https://github.com/peterderkoala/cinescout/issues/14) (cited as `#14:L<line>` of its
  body) plus `docs/design/Technical Design Spec.md` (cited as `TDS §n`).
- **Lens:** ponytail over-engineering (delete, then simplify, then replace with stdlib/native/installed
  dependency).

## Method and evidence sources

- **LOC.** Lines (including comments and doc-comments) of `.cs/.razor/.js/.css` per project, excluding
  `obj/`, `bin/` and vendored `wwwroot/lib`. EF migrations are counted separately.
- **CRG.** The code-review-graph was built at `e150686` (`head_matches_build: true`; 171 files, 2551 nodes).
  The checks used were `refactor_tool mode=dead_code` (880 hits, mostly false positives for DTOs/records,
  because the graph doesn't model type-usage edges, so every hit was checked with grep) and `query_graph
  callers_of`.
- **CRAP.** `./crap-check.sh` was **not run**. It needs `./coverage.sh` first, which means the whole
  Testcontainers suite, so it isn't cheap. CI (`22fda6d`, #113) already enforces CRAP ≤ 30 on every method in
  the six production projects, so no layer-1 finding here rests on a CRAP hotspot.
- **Historical baseline.** `git archive 3655430` is the last commit before the redesign. At that point every
  v1 screen (Watched Movies, Time Preferences, Seat Matrices, Schedule, Performance Detail, Login, Setup) was
  **static SSR** in `cinescout.web/Components/Pages`. It gives a real like-for-like size comparison, with
  caveats below.

### Size snapshot (`e150686`)

| Project | LOC | Files | Exists because of |
|---|---:|---:|---|
| cinescout.web | 2227 | 24 | host, endpoints, auth (#14:L51) |
| cinescout.web.Client | 2634 | 47 | WASM pages (#14:L51) |
| cinescout.contracts | 670 | 24 | WASM wire DTOs (#74 resolution, cost 2) |
| cinescout.core | 2316 | 32 | crawlers, matching, notify (#14:L53) |
| cinescout.model | 310 | 16 | entities (#14:L53) |
| cinescout.persistence | 163 (+1875 migrations) | 3 | DbContext (#14:L53, L57) |
| **production total** | **8320** | | |
| cinescout.web.Tests | 3284 | 12 | |
| cinescout.web.Client.Tests | 1252 | 7 | |
| cinescout.core.Tests | 4220 | 16 | |
| cinescout.contracts.Tests | 304 | 5 | |
| cinescout.persistence.Tests | 242 | 1 | |
| **test total** | **9302** | | |

There are 6 production and 5 test projects (11 in `src/cinescout.slnx`). #115's "7 production projects"
miscounts.

## Over-built (serves no spec line): verdicts

| # | Finding | Evidence | Spec trace | Verdict | Effort |
|---|---|---|---|---|---|
| O1 | **`PingApiClient` is dead production code.** It is registered in DI and never injected. | `src/cinescout.web.Client/Api/PingApiClient.cs` (24 LOC) plus the DI line at `src/cinescout.web.Client/Program.cs:17`. CRG `dead_code` lists it, and grep finds no `@inject`/ctor use, only doc-comment references. Its own doc says it is "not a real screen endpoint". | None. It was a #90 worked example, and the real clients (#92–#98) now carry the pattern. | **Cut** | XS |
| O2 | **`POST /api/ping` exists only for tests.** | `MapPingEndpoint`, `src/cinescout.web/Endpoints/ApiEndpoints.cs:29` (about 15 LOC with doc), registered in prod at `Program.cs:153`. Its only callers are `ApiHardeningTests.cs` (4 requests). | None. The antiforgery convention it demonstrates is ADR 0004's, but the convention doesn't need a dummy prod route to exist. | **Simplify.** Point `ApiHardeningTests` at a real mutating endpoint (e.g. `POST /api/tracked-movies/{filmId}/track`) and delete the route. | XS |
| O3 | **Two enums mirrored bit-for-bit from `cinescout.model`, plus a test that pins them in lockstep.** | `src/cinescout.contracts/Weekday.cs` (23), `src/cinescout.contracts/SeatOccupancyStatus.cs` (14), `src/cinescout.contracts.Tests/WeekdayBitLayoutParityTests.cs` (40), and `(Weekday)(int)` casts in `TimePreferencesEndpoints.cs` (3 sites). The mirror is justified only by the "contracts must never reference model" rule. But `src/cinescout.model/cinescout.model.csproj` has **zero** package or project references: it's pure POCO, with no EF Core or Npgsql to keep out of the browser. | #14:L58 asks for DTOs where needed. Nothing asks for a model-blind DTO project. #74's reason for the rule was EF/Npgsql in the browser, which applies to `persistence`, not `model`. | **Simplify.** Either let `contracts` reference `model` for the enums, or move the two enums into `contracts` and have `model` reference it. That deletes about 80 LOC and a class of drift bug. | XS |
| O4 | **Server-side DI registration of every WASM API client, only so prerender can construct them, plus `RendererInfo.IsInteractive` gates on 14 files.** | `AddClientApiClientsForPrerendering`, `src/cinescout.web/Extensions/WebServiceCollectionExtensions.cs:18-44` (about 27 LOC). Its own doc says "nothing on them is ever actually called". Every new screen must remember to add a line or it 500s (bug caught in #92). There are 14 `CardSkeleton` uses and 14 files gating on `IsInteractive`. | TDS §8 needs *a* loading state. No spec line asks for prerender. #74 lists "prerender double-run" as a cost, not a goal. | **Simplify (verify UX first).** Use `InteractiveWebAssemblyRenderMode(prerender: false)` on the WASM pages. That removes the server registrations and the double-init gate, and `CardSkeleton` stays as the loading state. The win is small; only do it if WASM stays. | S |

Nothing else in layer 1 qualifies as over-built under the evidence rule. The rest of the architecture is either
mandated by #14 or a direct, minimal consequence of a mandate, so it belongs in the next list.

## Spec-driven cost (the spec demands it; no verdict)

| # | Cost | Evidence | Spec line that demands it |
|---|---|---|---|
| S1 | **The hosted Blazor WASM client and the HTTP API it forces.** Every screen read and write crosses HTTP: 8 endpoint files, 9 client wrappers, and a DTO per screen. | `src/cinescout.web/Endpoints/*` **1329 LOC**; `src/cinescout.web.Client/Api/*` **353 LOC**; endpoint integration tests `src/cinescout.web.Tests/*EndpointsTests.cs` **2703 LOC**; client HTTP-path tests (`*SaveAsyncTests`, `CinemasConfirmRenameAsyncTests`) **928 LOC**. | #14:L51 ("Hosted Blazor WASM … global `InteractiveWebAssembly`"), #14:L110. #74's resolution re-affirmed it **against** #72's research recommendation of static SSR everywhere. |
| S2 | **`cinescout.contracts` as a separate leaf project** (DTOs, validation shared by client and server, day-code and date formatting). | 670 LOC / 24 files plus `cinescout.contracts.Tests` 304 LOC. It also cost a missed Dockerfile `COPY` (`0adfdaf`). | A consequence of S1 (#74 resolution, cost 2). Not demanded on its own: #14:L53 allows "an equivalent split". |
| S3 | **CSRF and session plumbing for a JSON API.** The `/api` antiforgery group filter, the `X-CSRF-TOKEN` header config, a `<meta>` token plus JS interop plus token store, `OnRedirectToLogin`/`AccessDenied` returning 401/403 for `/api`, and the `UseWhen` scoping of `UseStatusCodePagesWithReExecute` (a real bug caught in #90). | `ApiEndpoints.cs` (about 50 LOC excluding ping), `Program.cs:68-72,114-124`, `WebServiceCollectionExtensions.cs:60-85`, `App.razor:1-18,43-50`, `wwwroot/js/antiforgery.js`, `Api/AntiforgeryTokenStore.cs`, `Api/ApiClientBase.cs` (81), `ApiHardeningTests.cs` (169). That's about 400 LOC in total. | A consequence of S1 (ADR 0004). This is a **security measure**, so it is kept for as long as S1 stands. Static SSR would get it free from `<EditForm>` antiforgery. |
| S4 | **A stringly-typed Kinoheld outcome protocol across the wire.** The server maps `SeatFetchOutcome` to a `ProblemDetails` `kinoheldStatus` string, and the client parses it back into an alert. | `src/cinescout.web.Client/Api/KinoheldStatusParser.cs` (40) plus its tests (40), plus the ProblemDetails mapping in `PerformanceDetailEndpoints.cs` and `CinemasEndpoints.cs`. | A consequence of S1. The alert copy itself is TDS §5.7. Under SSR this would be a direct `switch` on the enum. |
| S5 | **Mapperly, used twice.** | `src/cinescout.web/Mapping/RoomMapper.cs` (23) and `SeatMatrixMapper.cs` (12), plus `RoomMapperTests.cs` (52) and a source-generator package. Every other DTO is hand-mapped by design (CLAUDE.md, contracts doc-comments). Two `new XDto { … }` lambdas would be about the same size. | #14:L58 ("Mapperly (not AutoMapper) wherever DTO↔entity mapping is needed"). It is cheap, but it buys almost nothing at two uses. |
| S6 | **The `model` / `persistence` / `core` three-way split.** | model 310 LOC / 16 files, persistence 163 LOC / 3 files (plus migrations), and a separate `persistence.Tests` (1 file, 242 LOC). There are 3 csproj files and 6 Dockerfile lines (`docker/Dockerfile:11-13,19-21`). | #14:L53 names the three projects but explicitly allows "or an equivalent split". This is soft-mandated. Merging all three into one project would save about 40–60 lines of csproj/Dockerfile/ProjectReference and nothing in code, so the payoff is navigational only. |
| S7 | **Hangfire scheduling** (layer-2 detail is #116's). Here it costs one dashboard plus Postgres job storage in the host's startup wiring. | `Program.cs` Hangfire wiring, `HealthChecks/HangfireHeartbeatHealthCheck.cs` (32) | #14:L54, L33 |

## Would an architecture-level reversal deliver the same spec with materially less code?

### Pure static SSR (no WASM, no `/api`, no `contracts`)

**Delivers the same spec: yes.** None of #14's 35 user stories (L11–L45) needs client-side interactivity.
TDS is render-mode-neutral:

- §2 says "dark mode is CSS-only … so static/SSR rendering is identical".
- §8 accepts any loading placeholder, which SSR satisfies trivially because it renders with data.
- The one "client-side" requirement, the §5.2 filter bar, is about 10 lines of vanilla JS hiding cards by
  `data-` attribute, or a GET form with query params. That is the pattern #72 found the framework documents.
  `password-toggle.js` already sets the precedent.

The pre-redesign tree at `3655430` *was* that architecture and delivered every v1 screen.

**Code delta (estimate):**

| Item | Today | Under SSR | Removed |
|---|---:|---:|---:|
| `Client/Api/*` client wrappers | 353 | 0 | 353 |
| CSRF/session API plumbing (S3, prod part) | ~230 | ~0 (built-in `EditForm` antiforgery) | ~230 |
| Prerender registration + mirrors + Mapperly (O3, O4, S5) | ~100 | 0 | ~100 |
| `Endpoints/*` (8 screen files) | 1263 | ~700 (queries move into page `OnInitializedAsync`; `TypedResults`/ProblemDetails/re-read-after-write/duplicate existence checks go) | ~550 |
| `contracts` DTOs | 670 | ~300 (card model, formatting, validation and day codes survive, moved into `web`) | ~370 |
| `KinoheldStatusParser` round-trip (S4) | 40 | ~15 | ~25 |
| Page code-behind/markup | ~1,500 | about the same (`[SupplyParameterFromForm]` + PRG instead of `@onclick` + API call) | ~0 |
| **Production subtotal** | | | **≈ 1,600 (~19% of 8,320)** |
| Client HTTP-path tests | 928 | 0 | 928 |
| Endpoint tests → page-handler tests | 2703 | ~1,600 (form-post tests through `WebApplicationFactory`, as `LoginTests` already does) | ~1,100 |
| `ApiHardeningTests` + `contracts.Tests` mirror/mapper tests | 169 + 92 | ~40 | ~220 |
| **Test subtotal** | | | **≈ 2,250 (~24% of 9,302)** |

**Total: ≈ 3,800 LOC (about 22% of the 17.6k non-migration LOC), plus 2 fewer production projects
(`web.Client`, `contracts`) and 2 fewer test projects.**

**Per-screen sanity check against history.** Seat Matrices was 349 LOC as one SSR file at `3655430`. Today it
is about 810: `SeatMatrices.razor` 261 + `.razor.cs` 242 + `SeatMatricesEndpoints.cs` 167 +
`SeatMatricesApiClient.cs` 31 + about 110 of DTO/validation/mapper. Time Preferences went from 241 to about
430. That ratio of roughly 2× overstates the WASM tax, because the redesign (TDS) also added the filter bar,
zone grid, precedence hint and design shell. It is consistent with the ~1.6k estimate, not with a much larger
one.

**What it would cost:**

- Re-opening #14:L51 and #74's resolution.
- Rewriting the 7 interactive screens' mutation flows, which are already written, reviewed and tested. That is
  roughly one ticket per screen plus a teardown ticket, so **L** overall.
- WASM's only user-visible upside is no full-page reload on filter, tab or force-refresh, and the spec doesn't
  ask for that.

**Bottom line.** The reversal is materially smaller (about a fifth of the codebase and 4 of 11 projects) and
spec-equivalent. But the bulk of the system (core crawling/matching at 2.3k and its 4.2k tests) doesn't depend
on the render mode, so this is a medium win bought with a large rewrite. Whether to buy it is a spec-reopening
call for #119, not a verdict this list can give.

### Merging `contracts` / `model` / `persistence` / `core`

- **While WASM stays.** `contracts` must stay browser-safe and separate from `persistence`. The only cheap win
  is O3 (let `contracts` see the dependency-free `model`).
- **Merging `model` + `persistence` + `core` into one project.** This removes 2 csproj files, 6 Dockerfile
  lines, the `persistence.Tests` project (fold its one file into `core.Tests`, which already uses
  Testcontainers) and a few `ProjectReference`s. That's **under 100 lines, no logic**. #14:L53 permits it. The
  effort is S, but every namespace changes. It is only worth doing as part of an SSR reversal, where `web` +
  one `core` would be the whole production solution (6 projects become 2).

## Out-of-layer findings handed to sibling tickets

These turned up during the layer-1 sweep and meet the evidence rule. They are listed here only so they aren't
lost. The verdicts belong to the named ticket.

- **→ #116: `IEmailSender`/`EmailSender` has no consumer.** `src/cinescout.core/Email/*` is 93 LOC, plus
  180 LOC of tests (`EmailSender*Tests.cs`), MailKit, `Microsoft.Extensions.Resilience`, and a Testcontainers
  SMTP server. CRG `callers_of EmailSender.SendAsync` returns only its own tests, and the DI registration at
  `CoreServiceCollectionExtensions.cs:66` is never injected. #42 says outright "No downstream consumer … is
  built here". #14 names only a Discord webhook (L104) and puts nothing email-related in scope. This is a
  strong **cut** candidate.
- **→ #116: `LegacyPasswordHashMigrator` is an upgrade bridge** from #14:L109's env-var hash to ADR 0001's
  `User` table. It is `src/cinescout.web/Auth/LegacyPasswordHashMigrator.cs` (38) plus tests (89). It is only
  worth keeping if a deployment still exists whose `User.PasswordHash` is null and that relies on
  `Auth__PasswordHash`. The operator needs to confirm that.
