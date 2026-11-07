#!/usr/bin/env bash
set -euo pipefail
root="$(cd "$(dirname "$0")/../../.." && pwd)"
if [[ "$#" != 1 || -e "$1" ]]; then
  printf 'Usage: %s new-output-directory\n' "$0" >&2
  exit 2
fi
mkdir -p "$1"
out="$(cd "$1" && pwd)"
build="$(mktemp -d)"
trap 'rm -rf "$build"' EXIT
cd "$root/runtime"
dotnet restore RuntimeKnowledge.slnx --locked-mode
for component in Api Worker; do
  dotnet publish "RuntimeKnowledge.$component" -c Release --no-restore -p:UseAppHost=false -o "$build/$component"
  package="$(printf '%s' "$component" | tr '[:upper:]' '[:lower:]')"
  (cd "$build/$component" && zip -qr "$out/$package.zip" .)
done
dotnet publish RuntimeKnowledge.Web -c Release --no-restore -o "$build/web"
mv "$build/web/wwwroot" "$out/wwwroot"
