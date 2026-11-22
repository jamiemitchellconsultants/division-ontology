#!/usr/bin/env bash
set -euo pipefail
dir="$(cd "$(dirname "$0")" && pwd)"
compose=(docker compose --env-file "$dir/test.env" -f "$dir/compose.yml")
web_settings_check='
  .services.api.environment as $api |
  .configs["web-settings"].content | fromjson |
  keys == ["EvidenceRepositories", "Identity"] and
  .Identity == {Authority: $api.Workspace__Identity__Issuer, ClientId: $api.Workspace__Docs__ClientId} and
  (.EvidenceRepositories | . == null or (type == "object" and all(.[];
    type == "string" and test("\\Ahttps://github[.]com/[A-Za-z0-9_.-]+/[A-Za-z0-9_.-]+\\z") and
    (split("/")[-2:] | all(. != "." and . != "..")))))
'

if [[ $# != 0 ]]; then
  if [[ $# != 2 || $1 != --env-file ]]; then
    printf 'Usage: %s [--env-file PATH]\n' "$0" >&2
    exit 2
  fi
  if ! docker compose --env-file "$2" -f "$dir/compose.yml" config --format json |
      jq -e "$web_settings_check" >/dev/null 2>&1; then
    printf 'Invalid web settings. WORKSPACE_EVIDENCE_REPOSITORIES must be unset or a JSON object of HTTPS GitHub owner/repository URLs. Identity must match the workspace API settings.\n' >&2
    exit 1
  fi
  exit 0
fi

# Ignore caller environment overrides when rendering the synthetic contract.
verify_config() {
  local evidence="$1"
  local -a settings=()
  if [[ -n "$evidence" ]]; then settings+=("WORKSPACE_EVIDENCE_REPOSITORIES=$evidence"); fi
  env -i PATH="$PATH" ${settings[@]+"${settings[@]}"} "${compose[@]}" config --format json | jq -e --argjson expected "${evidence:-null}" '
  ((.services | to_entries | map(select(.value.networks | has("ingress")) | .key) | sort) == ["api", "web"]) and
  (.networks.ingress.external and .volumes.database.external and .volumes.objects.external) and
  (.services | all(.[]; ((.ports // []) | length) == 0)) and
  (.services.db.volumes | any(.target == "/var/lib/postgresql/data" and .source == "database")) and
  (.services.minio.volumes | any(.target == "/data" and .source == "objects")) and
  (.services.api.environment.Workspace__Embeddings__Dimensions == "4096") and
  (.services.api.environment.Workspace__Embeddings__Endpoint == .services.indexer.environment.Workspace__Embeddings__Endpoint) and
  (.services.api.environment.Workspace__Identity__Issuer == "https://auth.example.test/realms/workspace") and
  (.services.api.environment.Workspace__Identity__AllowLocalHttp == null) and
  (.services.api.environment.Workspace__Identity__Audience == "workspace-api") and
  (.configs["web-settings"].content | fromjson |
    .Identity.ClientId == "workspace-spa" and
    (.EvidenceRepositories | . == $expected and
      (. == null or (type == "object" and all(.[]; test("^https://github[.]com/[A-Za-z0-9_.-]+/[A-Za-z0-9_.-]+$")))))) and
  (.services.api.environment.Workspace__Docs__ClientId == "workspace-spa")
'
}

verify_config ''
verify_config '{"fixture/ecommerce":"https://github.com/fixture/ecommerce"}'
for evidence in '' '{"fixture/ecommerce":"https://github.com/fixture/ecommerce"}'; do
  env -i PATH="$PATH" WORKSPACE_EVIDENCE_REPOSITORIES="$evidence" bash "$0" --env-file "$dir/test.env"
done
for invalid in 'https://github.com/fixture/ecommerce' '[]' '{"fixture/ecommerce":"https://example.test/fixture/ecommerce"}'; do
  if output=$(env -i PATH="$PATH" WORKSPACE_EVIDENCE_REPOSITORIES="$invalid" \
      bash "$0" --env-file "$dir/test.env" 2>&1); then
    printf 'Expected the evidence settings preflight to reject an invalid map.\n' >&2
    exit 1
  fi
  [[ "$output" == *'Invalid web settings.'* ]]
done
jq -e '
  ((.users // []) | length == 0) and
  (.roles.client["${WORKSPACE_KEYCLOAK_API_CLIENT}"] | map(.name) | sort == ["ManageKnowledgeGraph","ReadKnowledgeGraph"]) and
  (.clients | any(.publicClient and (.directAccessGrantsEnabled == false) and
    .attributes["pkce.code.challenge.method"] == "S256" and
    .redirectUris == ["https://${WORKSPACE_PUBLIC_HOST}/authentication/login-callback", "https://${WORKSPACE_PUBLIC_HOST}/api/docs/oauth2-redirect.html"]))
' "$dir/realm-template.json"
grep -Fq 'reverse_proxy runtime-workspace-api:8080' "$dir/Caddyfile.fragment"
grep -Fq 'reverse_proxy runtime-workspace-web:8080' "$dir/Caddyfile.fragment"
for key in WORKSPACE_DB_ADMIN_CONNECTION_STRING WORKSPACE_OBJECT_SECRET_KEY WORKSPACE_EMBEDDING_ENDPOINT WORKSPACE_EMBEDDING_REVISION; do
  missing="$(mktemp)"
  awk -F= -v key="$key" '$1 != key' "$dir/test.env" > "$missing"
  if env -i PATH="$PATH" docker compose --env-file "$missing" -f "$dir/compose.yml" config >/dev/null 2>&1; then
    rm -f "$missing"
    printf 'Compose should require %s\n' "$key" >&2
    exit 1
  fi
  rm -f "$missing"
done
