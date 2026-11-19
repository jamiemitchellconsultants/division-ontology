#!/usr/bin/env bash
# Checks, anonymously, that a deployment serves the workspace API spec and docs UI without opening any data.
# Usage: verify-docs.sh <origin> [--expect-sign-in]
set -euo pipefail

origin="${1:?Usage: verify-docs.sh <origin> [--expect-sign-in]}"
origin="${origin%/}"
expect_sign_in=false
[[ "${2:-}" == "--expect-sign-in" ]] && expect_sign_in=true

fail() { printf 'verify-docs: %s\n' "$1" >&2; exit 1; }
body="$(mktemp)"
trap 'rm -f "$body"' EXIT

curl -fsS "$origin/api/openapi/v1.json" -o "$body" || fail "the spec at $origin/api/openapi/v1.json was not served"
jq -e '.openapi | startswith("3.1")' "$body" >/dev/null || fail "the spec is not OpenAPI 3.1"
jq -e 'has("servers") | not' "$body" >/dev/null || fail "the served spec names a server address (ADR 0017)"
if $expect_sign_in; then
  jq -e '.components.securitySchemes.workspaceSignIn.flows.authorizationCode.authorizationUrl // "" | test("^https?://")' "$body" >/dev/null ||
    fail "the served spec has no workspaceSignIn authorization URL"
fi

for page in index.html oauth2-redirect.html; do
  status="$(curl -s -o /dev/null -w '%{http_code}' "$origin/api/docs/$page")"
  [[ "$status" == 200 ]] || fail "/api/docs/$page returned $status"
done

status="$(curl -s -o "$body" -w '%{http_code}' "$origin/api/inventory")"
[[ "$status" == 401 ]] || fail "an anonymous data call returned $status, not 401"
jq -e '.code == "authenticationRequired"' "$body" >/dev/null || fail "the 401 body is not authenticationRequired"

printf 'Docs verified at %s.\n' "$origin"
