#!/usr/bin/env bash
# Runs the full test suite (dotnet test src/cinescout.slnx). The persistence/core test projects
# spin up a real Postgres via Testcontainers, so Docker must be reachable. If the invoking shell's
# docker group membership hasn't taken effect yet (a fresh `usermod -aG docker` needs a new login
# session), this falls back to `sg docker -c "..."` automatically instead of failing outright.
set -euo pipefail
cd "$(dirname "${BASH_SOURCE[0]}")"

cmd=(dotnet test src/cinescout.slnx "$@")

if docker info >/dev/null 2>&1; then
    "${cmd[@]}"
else
    echo "Docker not reachable directly — retrying via 'sg docker' (see CLAUDE.md)." >&2
    printf -v quoted '%q ' "${cmd[@]}"
    sg docker -c "$quoted"
fi
