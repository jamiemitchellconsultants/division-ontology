#!/usr/bin/env bash
set -euo pipefail
root="$(cd "$(dirname "$0")/../../.." && pwd)"
scenario="$(mktemp -d)"
export WORKSPACE_SCENARIO_DIR="$scenario"
export WORKSPACE_VERIFY_APP_VERSION="$(git -C "$root" rev-parse --short HEAD)"
if [[ -n "$(git -C "$root" status --porcelain)" ]]; then
    WORKSPACE_VERIFY_APP_VERSION+="+working-tree"
fi
export COMPOSE_PROJECT_NAME="workspace-smoke-$(openssl rand -hex 5)"
export WORKSPACE_PORT="${WORKSPACE_PORT:-18087}"
export WORKSPACE_DB_ADMIN_PASSWORD="$(openssl rand -hex 24)"
export WORKSPACE_DB_API_PASSWORD="$(openssl rand -hex 24)"
export WORKSPACE_DB_PUBLISHER_PASSWORD="$(openssl rand -hex 24)"
export WORKSPACE_DB_INDEXER_PASSWORD="$(openssl rand -hex 24)"
export WORKSPACE_MINIO_ROOT_PASSWORD="$(openssl rand -hex 24)"
export WORKSPACE_DEV_MANAGER_PASSWORD="$(openssl rand -hex 24)"
export WORKSPACE_DEV_READER_PASSWORD="$(openssl rand -hex 24)"

compose=(docker compose --project-name "$COMPOSE_PROJECT_NAME" -f "$root/deploy/workspace/local/compose.yml")
# Verify the resolved project before creating or deleting any resources.
[[ "$COMPOSE_PROJECT_NAME" =~ ^workspace-smoke-[a-f0-9]{10}$ ]]
"${compose[@]}" config --format json | jq -e --arg name "$COMPOSE_PROJECT_NAME" '
    .name == $name and
    (.volumes | all(.[]; (.external != true) and (.name | startswith($name + "_"))))
' >/dev/null
cleanup() {
    status=$?
    trap - EXIT
    if [[ "$status" != 0 ]]; then "${compose[@]}" ps -a || true; fi
    "${compose[@]}" down --volumes --timeout 10 >/dev/null 2>&1 || true
    rm -rf "$scenario"
    exit "$status"
}
trap cleanup EXIT

"${compose[@]}" up --build -d --wait
"$root/deploy/workspace/local/bootstrap.sh"
"$root/deploy/workspace/verify-docs.sh" "http://workspace.localhost:$WORKSPACE_PORT" --expect-sign-in
"$root/deploy/workspace/verify.sh" mac
node "$root/deploy/workspace/verify-traversal-paging.mjs" mac
"${compose[@]}" restart api db
"${compose[@]}" up -d --wait
"$root/deploy/workspace/verify.sh" mac --check-persistence

replay="$(node "$root/deploy/workspace/verify-replay.mjs" "$(cat "$scenario/snapshot")" "${compose[@]:1}")"
position="$(jq -er '.active.throughPosition' <<< "$replay")"
"$root/deploy/workspace/verify.sh" mac --check-persistence
printf 'Mac replay digest matched at journal position %s.\n' "$position"
