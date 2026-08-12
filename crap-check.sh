#!/usr/bin/env bash
# Enforces the CRAP-score ceiling decided in issue #112 (part of wayfinder map #105): fails if any
# method across the six analyzed projects exceeds CRAP 30. Requires coverage.sh to have already run
# in this job — it reuses coverage/**/coverage.cobertura.xml rather than re-running the test suite.
#
# crap4dotnet (dotnet-crap) has no --exclude flag, so Migrations/ and Program.cs are filtered out of
# its JSON output afterward instead — they're generated/wiring code, not business logic (same
# exclusion rule the map's analysis tickets used). It also targets .NET 8; pinned in
# .config/dotnet-tools.json with "rollForward": true so it launches on this repo's .NET 10 runtime
# without needing DOTNET_ROLL_FORWARD set by hand. Its own --coverage flag silently drops per-class
# coverage when given multiple raw per-project Cobertura files instead of one merged file — hence
# the ReportGenerator merge step below.
set -euo pipefail
cd "$(dirname "${BASH_SOURCE[0]}")"

if ! compgen -G "coverage/*/coverage.cobertura.xml" >/dev/null; then
    echo "No coverage found under coverage/ — run ./coverage.sh first." >&2
    exit 1
fi

THRESHOLD=30
PROJECTS=(
    cinescout.web
    cinescout.core
    cinescout.persistence
    cinescout.model
    cinescout.contracts
    cinescout.web.Client
)

dotnet tool restore

rm -rf coverage/crap
mkdir -p coverage/crap
dotnet tool run reportgenerator \
    -reports:"coverage/**/coverage.cobertura.xml" \
    -targetdir:"coverage/crap" \
    -reporttypes:"Cobertura"

crappy_count=0
for project in "${PROJECTS[@]}"; do
    report="coverage/crap/${project}.json"
    dotnet tool run dotnet-crap analyze "src/${project}" \
        --coverage "coverage/crap/Cobertura.xml" \
        --threshold "$THRESHOLD" \
        --output "$report" || true

    project_crappy=$(jq '[.methods[]
        | select(((.filePath | contains("/Migrations/")) or (.filePath | endswith("Program.cs"))) | not)
        | select(.isCrappy)] | length' "$report")

    if [ "$project_crappy" -gt 0 ]; then
        echo "== $project: $project_crappy method(s) over CRAP $THRESHOLD =="
        jq -r '.methods[]
            | select(((.filePath | contains("/Migrations/")) or (.filePath | endswith("Program.cs"))) | not)
            | select(.isCrappy)
            | "  CRAP \(.crap)\t\(.coverage)% cov\t\(.fullName)  (\(.filePath):\(.lineNumber))"' "$report"
    fi

    crappy_count=$((crappy_count + project_crappy))
done

if [ "$crappy_count" -gt 0 ]; then
    echo
    echo "$crappy_count method(s) exceed the CRAP $THRESHOLD ceiling — see above."
    exit 1
fi

echo "All projects under the CRAP $THRESHOLD ceiling."
