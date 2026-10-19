# Issue tracker: GitHub

GitHub Issues on `peterderkoala/cinescout`, via the `gh` CLI. "Publish to the issue tracker" = create an issue.

**PRs as a request surface: no.** _(`/triage` reads this flag.)_

## Wayfinding operations

Used by `/wayfinder`. The **map** is a single issue with **child** issues as tickets.

- **Map**: an issue labelled `wayfinder:map`, holding the Notes / Decisions-so-far / Fog body.
- **Child ticket**: linked to the map as a GitHub sub-issue (`gh api` sub-issues endpoint). Labels:
  `wayfinder:<type>` (`research`/`prototype`/`grilling`/`task`). Once claimed, assigned to the driving dev.
- **Blocking**: GitHub's native issue dependencies — `gh api --method POST
  repos/<owner>/<repo>/issues/<child>/dependencies/blocked_by -F issue_id=<blocker-db-id>`, where
  `<blocker-db-id>` is the numeric **database id** (`gh api repos/<owner>/<repo>/issues/<n> --jq .id`), not the
  `#number` or `node_id`. `issue_dependencies_summary.blocked_by` counts open blockers only — the live gate.
- **Frontier query**: the map's open children, minus any with an open blocker or an assignee; first in map
  order wins.
- **Claim**: `gh issue edit <n> --add-assignee @me` — the session's first write.
- **Resolve**: comment the answer, close, then append a gist + link to the map's Decisions-so-far.
