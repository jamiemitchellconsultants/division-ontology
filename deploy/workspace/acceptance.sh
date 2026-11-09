#!/usr/bin/env bash
set -uo pipefail
umask 077
root="$(cd "$(dirname "$0")/../.." && pwd)"
target="${1:-}"
if [[ "$#" -lt 2 || "$#" -gt 3 ||
    ( "$target" != local && "$target" != home-lab ) ||
    ( "$#" == 3 && ( "$target" != home-lab || "$3" != --check-persistence ) ) ]]; then
    printf 'Usage: %s local|home-lab NEW_EVIDENCE_DIRECTORY [--check-persistence]\n' "$0" >&2
    exit 2
fi
mkdir -- "$2" || exit 2
evidence="$(cd -- "$2" && pwd)"
cd "$root" || exit 2
results="$evidence/checks.tsv"
printf 'check\tstatus\tevidence\n' > "$results"
{
    printf 'target=%s\nrevision=%s\n' "$target" "$(git rev-parse HEAD)"
    printf 'started=%s\n' "$(date -u +%FT%TZ)"
    if [[ -n "$(git status --porcelain)" ]]; then printf 'workingTree=modified\n'; fi
} > "$evidence/run.txt"
errors=0

run() {
    local name="$1" log
    shift
    log="$evidence/$name.log"
    if "$@" > "$log" 2>&1; then
        printf '%s\tpass\t%s\n' "$name" "$name.log" >> "$results"
    else
        printf '%s\tfail\t%s\n' "$name" "$name.log" >> "$results"
        printf 'FAIL %s. See %s\n' "$name" "$log" >&2
        errors=1
    fi
}

runtime_tests() (
    cd "$root/runtime" || exit 2
    "$root/deploy/workspace/database/test-db.sh" run dotnet test RuntimeKnowledge.slnx \
        --logger 'trx;LogFileName=runtime.trx' --results-directory "$evidence"
)

if [[ "$target" == local ]]; then
    run repository npm test
    run runtime runtime_tests
    run home-lab-config bash deploy/workspace/home-lab/verify-config.sh
    run local-browser bash deploy/workspace/local/smoke.sh
else
    required=(WORKSPACE_SCENARIO_DIR WORKSPACE_VERIFY_ORIGIN WORKSPACE_VERIFY_MODEL_REVISION
        WORKSPACE_VERIFY_APP_VERSION WORKSPACE_VERIFY_MANAGER_USERNAME WORKSPACE_VERIFY_MANAGER_PASSWORD
        WORKSPACE_VERIFY_READER_USERNAME WORKSPACE_VERIFY_READER_PASSWORD)
    for key in "${required[@]}"; do
        if [[ -z "${!key:-}" ]]; then
            printf 'BLOCKED home-lab. Set %s in the runner environment.\n' "$key" >&2
            errors=1
        fi
    done
    if (( errors )); then
        printf 'deployed-browser\tblocked\tmissing fixture configuration\n' >> "$results"
    elif [[ ! -s "$WORKSPACE_SCENARIO_DIR/snapshot" ||
        ( "$#" == 3 && ! -s "$WORKSPACE_SCENARIO_DIR/scenario.json" ) ]]; then
        printf 'deployed-browser\tblocked\tmissing fixture state\n' >> "$results"
        printf 'BLOCKED home-lab. Supply the fixture snapshot and, after restart, the initial scenario record.\n' >&2
        errors=1
    else
        run deployed-browser bash deploy/workspace/verify.sh home-lab "${@:3}"
    fi
fi

printf 'B14\thuman review\tindependent reviewer explanation needed\n' >> "$results"
printf 'REVIEW B14. Record the independent reviewer’s explanation in 05-acceptance.md.\n' >&2
printf 'Evidence: %s\n' "$evidence"
exit "$errors"
