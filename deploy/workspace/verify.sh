#!/usr/bin/env bash
set -euo pipefail
root="$(cd "$(dirname "$0")/../.." && pwd)"
case "${1:-}" in
    mac)
        shift
        exec node "$root/deploy/workspace/local/verify-browser.mjs" "$@"
        ;;
    *)
        printf 'Usage: %s mac [--check-persistence]\n' "$0" >&2
        exit 2
        ;;
esac
