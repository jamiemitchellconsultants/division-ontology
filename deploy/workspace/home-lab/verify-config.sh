#!/usr/bin/env bash
set -euo pipefail
dir="$(cd "$(dirname "$0")" && pwd)"
compose=(docker compose --env-file "$dir/test.env" -f "$dir/compose.yml")
# Ignore caller environment overrides when rendering the synthetic contract.
env -i PATH="$PATH" "${compose[@]}" config --format json | jq -e '
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
  (.configs["web-settings"].content | fromjson | .Identity.ClientId == "workspace-spa") and
  (.services.api.environment.Workspace__Docs__ClientId == "workspace-spa")
'
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
