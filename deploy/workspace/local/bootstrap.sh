#!/usr/bin/env bash
set -euo pipefail
root="$(cd "$(dirname "$0")/../../.." && pwd)"
compose=(docker compose -f "$root/deploy/workspace/local/compose.yml")
if [[ -n "${COMPOSE_PROJECT_NAME:-}" ]]; then
    compose+=(--project-name "$COMPOSE_PROJECT_NAME")
fi
if [[ "$#" == 2 ]]; then
    bundle="$(cd "$1" && pwd)"
    manifest="$2"
elif [[ "$#" == 0 ]]; then
    temporary="$(mktemp -d)"
    trap 'rm -rf "$temporary"' EXIT
    bundle="$temporary/bundle"
    manifest="$(node "$root/src/workspace/seed-fixture.mjs" "$temporary/checkouts" "$bundle")"
else
    printf 'Usage: %s [bundle-directory manifest-digest]\n' "$0" >&2
    exit 2
fi
if [[ ! "$manifest" =~ ^[a-f0-9]{64}$ || ! -f "$bundle/manifests/$manifest.json" ]]; then
    printf 'Supply a bundle and its manifest digest.\n' >&2
    exit 2
fi
"${compose[@]}" run --rm --no-deps publisher bucket-init
"${compose[@]}" run --rm --no-deps -v "$bundle:/bundle:ro" publisher upload /bundle
"${compose[@]}" run --rm --no-deps publisher stage "$manifest"
for ((attempt=1; attempt<=40; attempt++)); do
    if result="$("${compose[@]}" run --rm --no-deps publisher activate "$manifest" 2>&1)"; then
        printf '%s\n' "$result"
        break
    fi
    if [[ "$attempt" == 40 ]] || ! grep -q '^snapshotIndexPending:' <<< "$result"; then
        printf '%s\n' "$result" >&2
        exit 1
    fi
    sleep 3
done
if [[ -n "${WORKSPACE_SCENARIO_DIR:-}" ]]; then
    printf '%s\n' "$manifest" > "$WORKSPACE_SCENARIO_DIR/snapshot"
fi
printf 'Active workspace snapshot %s\n' "$manifest"
