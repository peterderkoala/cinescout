# Agent and docs overhead audit against the v1 spec

Resolves the research question in [#118](https://github.com/peterderkoala/cinescout/issues/118) (map
[#114](https://github.com/peterderkoala/cinescout/issues/114)). Audited at `origin/dev` `e150686`, 2026-10-13.

**Yardstick:** the v1 spec [#14](https://github.com/peterderkoala/cinescout/issues/14), plus
`docs/design/Technical Design Spec.md` (TDS) and later wayfinder decisions (#74, #78, #100).
**Lens:** ponytail (delete / simplify / replace with the environment) and *writing-for-agents*: an
always-loaded file is a **cache** of the environment. It earns its bytes only for what the agent can't find by
looking: gotchas, reasons, and unwritten conventions.

**Method:** byte and line counts come from `wc`, and section sizes from an awk pass over headings. Staleness
was checked against the source with `grep`/`ls`/`git log -S`. Issue states were checked with `gh`. CRAP and
CRG metrics don't apply here because nothing in scope is code. Every finding below names a file, a metric, and
a spec trace.

## Footprint

| Artifact | Size | Loaded |
|---|---|---|
| `CLAUDE.md` | 28,982 B / 344 lines | every turn |
| └ `## Project status` section | 22,408 B (77%) | every turn |
| └ `## MCP Tools: code-review-graph` section | ~3,774 B | every turn |
| `~/CLAUDE.md` (CRG block) + `~/.claude/CRG-RULES.md` | 2,345 B + 1,206 B | every turn (outside repo) |
| `README.md` | 8,500 B / 176 lines | on demand |
| `docs/design/*.md` per-screen + component docs (11 files) | ~48 KB | on demand |
| `docs/design/Technical Design Spec.md` | 20,159 B / 350 lines | on demand |
| `docs/design/*.dc.html` + `data.js`/`support.js`/`image-slot.js` | ~242 KB | never by agents (prototype) |
| `docs/adr/` (5 files) | 21,940 B | on demand |
| `docs/CONTEXT.md` | 4,298 B / 49 lines | on demand |
| `docs/agents/` (2 files) | 6,375 B | on demand (skills) |
| `.claude/skills/` (4 CRG-generated) | 4,631 B | descriptions every turn |
| `.claude/settings.json` + `.bak` | 901 B ×2 | harness |
| `handoff/handoff.md` (git-ignored) | 4,755 B | on demand |

---

## Incidental findings (outside the docs question, but blocking)

These came up while checking staleness. They're bigger than anything else in this audit.

1. **CI and release workflows were deleted by commit `5e7134f` ("added crg").** That commit's diffstat shows
   `.github/workflows/ci.yml | 64 ---` and `release.yml | 46 ---`, and `git ls-tree origin/dev` and
   `origin/main` both have no `.github/` at all. `CLAUDE.md:160` still says "`ci.yml` runs `./run-tests.sh` on
   every PR/push", and the CRAP ceiling from #113 depended on that workflow. It looks accidental. Restore it
   from `22fda6d`.
2. **No page declares a render mode.** `grep -rn rendermode src` returns only a comment in `App.razor`, and
   `git log -S"@rendermode" -- src` shows the only uses were the deleted template `Counter` page and #16.
   `CLAUDE.md:17` claims "render mode is set **per page** (`@rendermode InteractiveWebAssembly`)". Pages such as
   `Schedule.razor` gate their fetch on `RendererInfo.IsInteractive`. If nothing goes interactive, they would
   show `CardSkeleton` forever. This is unverified in a browser, so treat it as a possible functional bug and
   check it with `/run` before anything else.

---

## Over-built (verdict + rationale)

| # | File / section | Metric and evidence | Spec trace | Verdict |
|---|---|---|---|---|
| O1 | `CLAUDE.md` `## Project status` | 22,408 B, 77% of the file. It reads as one file-by-file narration of what each class does, with ticket numbers (#22, #23, #42, #89, #90, #99…). Most of it restates the source (class names, folder contents, which test project tests what). | #14 asks for none of it. The few gotchas inside it trace to spec features (see S-list). | **Simplify.** Keep only the gotchas and conventions (lean sketch below). That cuts about 22 KB to about 4 KB per turn. |
| O2 | `CLAUDE.md:37-83` (static-SSR passage) | About 3.2 KB is stale. It places `Schedule.razor`/`TrackedMovies.razor` in `cinescout.web/Components/Pages` and describes `EditForm`/`[SupplyParameterFromForm]`, `?editId=`, `?tab=` and POST-redirect-GET. `ls` shows all 5 screens in `cinescout.web.Client/Pages/`, and `grep -rlE "EditForm\|SupplyParameterFromForm" src` finds 0 hits. This is the known #78 staleness, 5 "static-SSR" mentions. | Superseded by #74/#78 (closed). | **Cut.** Keep one line: Login/Setup are static SSR because `SignInAsync` needs `HttpContext`. |
| O3 | `CLAUDE.md:17` "render mode is set per page" | False claim. No `@rendermode` exists in `src` (see incidental 2). | #14 said "global InteractiveWebAssembly". #74 changed that to per page. Neither matches the code. | **Cut** the claim. Re-add it once the render mode is actually wired. |
| O4 | `CLAUDE.md:160-163` CI/release paragraph | Stale on two counts. `ci.yml` no longer exists (incidental 1), and "VPS via a self-hosted runner" contradicts open map #100 (Watchtower + GHCR, no self-hosted runner). | #14 lists CI/CD as out of scope. #100 is the live decision. | **Simplify** to one line: "CI: `.github/workflows/ci.yml` runs `./run-tests.sh` + `crap-check.sh`", once that file is restored. |
| O5 | `CLAUDE.md:44` "`PingApiClient` … the one concrete pattern" and `:149` "currently just `KinoheldStatusParserTests`" | Stale counts. `src/cinescout.web.Client/Api/` holds 11 files (Cinemas/Home/Schedule/SeatMatrices/… clients plus `ApiClientBase`). `web.Client.Tests` holds 7 test files. | None, this is a status snapshot. | **Cut.** "Currently just X" lines go stale with the next PR. |
| O6 | `CLAUDE.md:106` "until #19's own `.env` var … is actually filled in" | #19 is closed. This is status-tense prose about a past state. | US-13/14 (Discord). The fact itself, an unconfigured URL is a clean no-op, still holds. | **Simplify** to the one timeless fact. |
| O7 | `CLAUDE.md` `## Project idea` + `### References` | 1,376 B. It labels itself as already implemented ("all of it now implemented"). It duplicates #14's Problem/Solution and README `## Status`. | It's an earlier copy of #14. | **Cut.** The API URL plus fixture path (`docs/api/hall-of-fame/filtered-films.json`) is the only cache worth one line. |
| O8 | `CLAUDE.md:7` "implementation is in progress against that ticket list" | #14 is still OPEN, but all features in the Project-idea list are claimed done. Map #114 is now a complexity review. | n/a | **Simplify** to a pointer: "spec #14; current work: open wayfinder maps". |
| O9 | `CLAUDE.md` `## Repository structure` | 879 B. It duplicates README `### Repository layout` almost line for line, and README already links to CLAUDE.md. | n/a | **Cut.** One pointer to README#architecture. |
| O10 | `CLAUDE.md` `## MCP Tools: code-review-graph` | ~3.8 KB. The same tool table and workflow is in `~/CLAUDE.md` (2,345 B) and the rules in `~/.claude/CRG-RULES.md`, so it loads 2 to 3 times every turn. The repo-specific extras ("code review start rule", "implementation rule", "wayfinder rule") restate CRG-RULES.md. "The graph auto-updates on file changes (via hooks)" and "run `code-review-graph update --brief`" duplicate the `.claude/settings.json` PostToolUse hook. | Not spec. This is tooling. | **Cut** from the repo file. The global files already cover it. If teammates without the global file matter, keep a 1-line pointer. |
| O11 | `.claude/settings.json.bak` | 901 B, byte-identical to `settings.json` (`diff` reports "identical"). Committed in `5e7134f`. | n/a | **Cut.** |
| O12 | `.claude/skills/{debug-issue,explore-codebase,refactor-safely,review-changes}` | 4,631 B, CRG-generated generic step lists (`list_graph_stats` → `get_architecture_overview_tool` → …) with nothing CineScout-specific. Their descriptions sit in the skill listing every turn, and `review-changes` overlaps the `code-review` skill the user's rules default to. | n/a | **Cut** (or git-ignore them as CRG install output). The tool table already teaches the same order. |
| O13 | `.mcp.json` (tracked) | Hard-codes `/home/ryzen/.local/share/pipx/...` and `cwd: /home/ryzen/git/cinescout`. It doesn't work on any other machine or worktree. | n/a | **Simplify.** Git-ignore it or move it to user-scope MCP config. |
| O14 | `docs/design/*.md` per-screen docs | 8 of 11 contain "static-SSR"/"static SSR" mechanism text (`grep -lic`): `00-design-system`, `auth`, `layout-navigation`, `performance-detail`, `schedule`, `seat-matrices`, `time-preferences`, `tracked-movies`. #78 §2 ruled that text void, ranked TDS/the prototype above these docs, and left the docs' fate as fog. All screens are now built in `cinescout.web.Client`, so the business rules they held now live in code and tests. | Written for the #61 design map. TDS supersedes them. | **Simplify now, cut later.** Add a one-line "superseded by TDS; render-mode text void (#78)" banner. Delete each one once its business rules are confirmed by a test. |
| O15 | `docs/agents/domain.md` | 2,630 B / 66 lines. Mostly stock skill boilerplate: a generic "stock convention" file tree, `CONTEXT-MAP.md` multi-context rules, and "proceed silently" if files are missing. The repo-specific content fits in 2 lines: CONTEXT.md lives at `docs/CONTEXT.md`, and ADRs live in `docs/adr/`. | n/a | **Simplify** to the 2 repo-specific lines. |
| O16 | `docs/agents/issue-tracker.md` | 3,745 B. Most of it is `gh` usage the model already knows (a no-op by the writing-for-agents test). The live parts are the wayfinder map/child/blocking conventions and "PRs as request surface: no". | n/a | **Simplify** to the wayfinder conventions plus the PR flag. |
| O17 | `handoff/handoff.md` (git-ignored) | 4,755 B, describes `22fda6d`. dev is 2 commits ahead (`5e7134f`, `e150686`), and that gap includes the CI deletion it doesn't know about. It also cites a "green CI run" that can't recur now. CLAUDE.md spends 431 B telling agents to read it "first". | n/a | **Keep** the mechanism, since it's cheap and on demand. **Simplify** the CLAUDE.md pointer to one line. A handoff is only trusted after checking `git log` since its commit. |

## Spec-driven cost (no verdict)

These are costs the spec or a later binding decision demands. They're listed so they don't get mistaken for
bloat.

| # | Artifact | Metric | Traces to |
|---|---|---|---|
| S1 | `docs/api/hall-of-fame/filtered-films.json` | 1,178,008 B (largest file in `docs/`) | #14 Testing Decisions: canned Hall-of-Fame JSON fed through `IHallOfFameClient` mocks. The fixture path is named in the spec. |
| S2 | `docs/design/Technical Design Spec.md` | 20,159 B / 350 lines | Named as part of the yardstick in #118. It's the design-to-code contract, and #78 ranks it above the older `.md` docs. |
| S3 | `docs/design/*.dc.html` + `support.js` (69 KB, "GENERATED … do not edit") + `image-slot.js` (65 KB) + `data.js` | ~242 KB | TDS line 6 names the prototype `.dc.html` files the design source of truth. The JS runtime is needed to render them. Agents never need to read it. |
| S4 | `docs/design/github.md` | 4,077 B | The design-tool sync manifest (repo/branch/last-sync commit). It's tool-owned metadata for S3. |
| S5 | `docs/adr/0001`, `0002` | 1,167 B + 3,047 B | Deliberate deviations from #14. #14 says "no user table" and ADR 0001 seeds one. #14 says `Site`/`WatchedMovie` and ADR 0002 renames them. Without these, an agent reading #14 would "fix" the code back. |
| S6 | `docs/adr/0003`–`0005` | 4,175 + 6,994 + 6,557 B | `Cinema.LastCrawlAt`; API auth/CSRF for WASM, which follows from #14's hosted WASM, US-25 and #74; Cinemas screen semantics. They're decisions, not derivable from code. |
| S7 | `docs/CONTEXT.md` | 4,298 B / 49 lines | #14 Further Notes: "consult CONTEXT.md before introducing new terms." |
| S8 | `CLAUDE.md` gotchas: `UseStatusCodePagesWithReExecute` scoped off `/api` (401 vs 302); `X-CSRF-TOKEN` header name; `IRecurringJobManager` not the static `RecurringJob`; `UseSetting` not `ConfigureAppConfiguration` for the test connection string; Postgres 18 mount at `/var/lib/postgresql`; non-root `--chown` + pre-created DataProtection dir; Kinoheld retry excludes 403/429 so the breaker trips | about 4 KB in total | US-25/26 (auth, cookie), US-33 (Hangfire), US-28 (Compose), US-20/35 (breaker, resilience). Each one was found by running the app and can't be read from config. This is exactly what a CLAUDE.md is for. |
| S9 | `CLAUDE.md` first-run/`User`-table setup narrative (`:191-206`) | ~2.4 KB | US-25/27 plus ADR 0001. It partly duplicates README "First-run setup" and ADR 0001, so only the invariant ("null `PasswordHash` = not set up; `Auth__PasswordHash` is upgrade-only") needs to stay. |
| S10 | `.claude/settings.json` PostToolUse/SessionStart hooks | 901 B | Not spec. They're the user's chosen CRG tooling, listed only because they make O10's "run update after implementing" redundant. |

---

## Lean CLAUDE.md sketch (headings + one line each)

Target is about 5 KB, down from 28.9 KB. Everything not listed here is either in the source, in README, or in
the global CRG files.

1. **Pointers.** Spec #14, glossary `docs/CONTEXT.md`, decisions `docs/adr/`, design contract `docs/design/Technical Design Spec.md` (outranks the older per-screen `.md`, #78), architecture in README#architecture, `handoff/handoff.md` (check `git log` since its commit).
2. **Commands.** `dotnet build src/cinescout.slnx`; `./run-tests.sh [--filter …]` (Docker via `sg docker` fallback); `./coverage.sh`; `./crap-check.sh` (ceiling 30); `docker compose -f docker/docker-compose.yml up`. No lint is configured.
3. **Project boundaries.** `web.Client` never references persistence/core. `contracts` is a true leaf. Mapperly mappers live in `cinescout.web/Mapping/`, and hand-written mapping covers anything not 1:1.
4. **Render modes.** Login/Setup are static SSR (`SignInAsync` needs `HttpContext`, `@layout AuthLayout`). Every other screen is WASM and talks to `/api` via `ApiClientBase` (state the actual mechanism once incidental 2 is fixed).
5. **`/api` conventions.** Antiforgery filter on the group (`X-CSRF-TOKEN`, token from `<meta>`). 401/403 instead of a redirect. `UseStatusCodePages` scoped off `/api` (the gotcha). `ProblemDetails` `kinoheldStatus`.
6. **Time.** `Performance.StartsAt` is Berlin wall-clock stored as a UTC instant. Convert via `CinemaTimeZone`. DTOs carry `DateTime` Unspecified. Npgsql `timestamptz` needs offset 0 (`.ToUniversalTime()`).
7. **Kinoheld posture.** Honest UA. Breaker trips on 403/429/anomalous and stays tripped until restart. Retry excludes 403/429. 400/404 are expected outcomes. The 30s cooldown applies to force-refresh only.
8. **Testing conventions.** Testcontainers Postgres, never a mocked `DbContext`. NSubstitute only at the 3 HTTP seams. Pure matchers, no bUnit. `UseSetting` for the test connection string. The `Testing` env skips Hangfire and migrations.
9. **Runtime gotchas.** Use `IRecurringJobManager`, not the static `RecurringJob`. `ConnectionStrings__Postgres` is required outside Testing. Startup-wiring changes must be run against real Postgres, because `dotnet test` misses them.
10. **Docker gotchas.** Postgres 18 mount is `/var/lib/postgresql`. Non-root `--chown` plus the pre-created DataProtection-Keys volume. The `Dockerfile.dockerignore` context is the repo root.
11. **Auth invariant.** A null `User.PasswordHash` means not set up, and setup goes through `/setup` plus the startup-logged token (ADR 0001). `Auth__PasswordHash` is upgrade-only.
12. **Agent skills.** Issue tracker: GitHub via `gh`, wayfinder conventions in `docs/agents/issue-tracker.md`.

Not carried over: per-class narration (the source), the project idea (#14), repository structure (README), the
CRG tool table (global `~/CLAUDE.md`), "currently just X" counts (they go stale), and static-SSR form patterns
(void per #78).
