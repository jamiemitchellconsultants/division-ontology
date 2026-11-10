#!/usr/bin/env bash
set -euo pipefail
root="$(cd "$(dirname "$0")/../../.." && pwd)"
tmp="${TMPDIR:-/tmp}"
state="${WORKSPACE_DEMO_DIR:-${tmp%/}/division-workspace-demo}"
state="${state%/}"
env_file="$state/demo.env"
marker="$state/.division-workspace-demo"

usage() {
    printf 'Usage: %s up|down|status\n' "$0" >&2
    exit 2
}

compose() {
    docker compose --project-name "$COMPOSE_PROJECT_NAME" -f "$root/deploy/workspace/local/compose.yml" "$@"
}

load_state() {
    if [[ ! -f "$marker" || ! -f "$env_file" ]]; then
        printf 'No demo workspace found at %s.\n' "$state" >&2
        exit 1
    fi
    set -a
    # shellcheck disable=SC1090
    source "$env_file"
    set +a
}

# Refuse to touch any compose project or volume that this script did not name.
check_project() {
    [[ "$COMPOSE_PROJECT_NAME" =~ ^workspace-demo-[a-f0-9]{10}$ ]]
    compose config --format json | jq -e --arg name "$COMPOSE_PROJECT_NAME" '
        .name == $name and
        (.volumes | all(.[]; (.external != true) and (.name | startswith($name + "_"))))
    ' >/dev/null
}

print_access() {
    printf '\nWorkspace: http://workspace.localhost:%s\n' "$WORKSPACE_PORT"
    printf 'Sign in as workspace-manager (edit) or workspace-reader (read only).\n'
    printf 'Passwords: WORKSPACE_DEV_MANAGER_PASSWORD and WORKSPACE_DEV_READER_PASSWORD in %s\n' "$env_file"
    printf 'Remove everything with: npm run demo:down\n'
}

up() {
    if [[ -e "$state" ]]; then
        printf 'A demo already exists at %s. Run npm run demo:down first.\n' "$state" >&2
        exit 1
    fi
    mkdir -p "$state"
    chmod 700 "$state"
    touch "$marker"
    (
        umask 077
        {
            printf 'COMPOSE_PROJECT_NAME=workspace-demo-%s\n' "$(openssl rand -hex 5)"
            printf 'WORKSPACE_PORT=%s\n' "${WORKSPACE_PORT:-8087}"
            for name in DB_ADMIN DB_API DB_PUBLISHER DB_INDEXER MINIO_ROOT DEV_MANAGER DEV_READER; do
                printf 'WORKSPACE_%s_PASSWORD=%s\n' "$name" "$(openssl rand -hex 24)"
            done
        } > "$env_file"
    )
    load_state
    check_project
    compose up --build -d --wait
    "$root/deploy/workspace/local/bootstrap.sh"
    print_access
}

down() {
    load_state
    check_project
    compose down --volumes --timeout 10
    rm -rf "$state"
    printf 'Removed the demo workspace, its data volumes and %s.\n' "$state"
}

status() {
    load_state
    check_project
    compose ps
    print_access
}

case "${1:-}" in
    up) up ;;
    down) down ;;
    status) status ;;
    *) usage ;;
esac
