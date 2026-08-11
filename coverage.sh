#!/usr/bin/env bash
# Runs the full test suite with code coverage collection (coverlet.collector, already referenced
# by every test project — dotnet new xunit's default) and merges the per-project Cobertura reports
# into one combined HTML + text-summary report via ReportGenerator (a local tool, see
# .config/dotnet-tools.json — `dotnet tool restore` before first use). Reading a single project's
# raw coverage.cobertura.xml on its own is misleading: e.g. cinescout.web shows up in
# cinescout.contracts.Tests' report too (trivially, via RoomMapperTests) alongside its real
# coverage from cinescout.web.Tests — merging is what makes the number honest. Mirrors
# run-tests.sh's Docker-reachability fallback — the persistence/core/web test projects still need
# Docker for their Testcontainers-backed Postgres.
set -euo pipefail
cd "$(dirname "${BASH_SOURCE[0]}")"

rm -rf coverage
cmd=(dotnet test src/cinescout.slnx --collect:"XPlat Code Coverage" --results-directory coverage "$@")

if docker info >/dev/null 2>&1; then
    "${cmd[@]}"
else
    echo "Docker not reachable directly — retrying via 'sg docker' (see CLAUDE.md)." >&2
    printf -v quoted '%q ' "${cmd[@]}"
    sg docker -c "$quoted"
fi

dotnet tool restore
dotnet tool run reportgenerator \
    -reports:"coverage/**/coverage.cobertura.xml" \
    -targetdir:"coverage/report" \
    -reporttypes:"Html;TextSummary"

echo
cat coverage/report/Summary.txt
echo
echo "Full HTML report: coverage/report/index.html"
