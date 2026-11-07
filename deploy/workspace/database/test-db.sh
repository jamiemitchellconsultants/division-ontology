#!/usr/bin/env bash
set -euo pipefail

root="$(cd "$(dirname "$0")" && pwd)"
image=runtime-knowledge-workspace-db:test

start() {
    docker build --tag "$image" "$root" >&2
    local container port
    container="$(docker run --rm -d -e POSTGRES_USER=workspace_test_admin \
        -e POSTGRES_PASSWORD -p 127.0.0.1::5432 "$image")"
    for ((attempt=0; attempt<30; attempt++)); do
        if docker exec "$container" pg_isready -q -h 127.0.0.1 -U workspace_test_admin; then
            port="$(docker port "$container" 5432/tcp)"
            printf '%s %s\n' "$container" "${port##*:}"
            return
        fi
        sleep 1
    done
    docker stop "$container" >&2
    return 1
}

case "${1:-}" in
    start)
        : "${POSTGRES_PASSWORD:?export a password for the disposable local test container}"
        read -r container port < <(start)
        printf 'postgresql://workspace_test_admin@127.0.0.1:%s/postgres\n' "$port"
        printf 'Stop this disposable container with: docker stop %s\n' "$container" >&2
        ;;
    run)
        shift
        if (( $# == 0 )); then
            printf 'Usage: %s run COMMAND [ARG ...]\n' "$0" >&2
            exit 2
        fi
        export POSTGRES_PASSWORD="$(openssl rand -hex 24)"
        read -r container port < <(start)
        trap 'docker stop "$container" >/dev/null' EXIT
        export WORKSPACE_TEST_POSTGRES="Host=127.0.0.1;Port=$port;Database=postgres;Username=workspace_test_admin;Password=$POSTGRES_PASSWORD"
        "$@"
        ;;
    *)
        printf 'Usage: %s start | run COMMAND [ARG ...]\n' "$0" >&2
        exit 2
        ;;
esac
