#!/usr/bin/env bash
set -euo pipefail
root="$(cd "$(dirname "$0")/../.." && pwd)"
case "${1:-}" in
    mac|home-lab)
        if [[ "$#" -gt 2 || ( "$#" == 2 && "$2" != --check-persistence ) ]]; then exit 2; fi
        exec node "$root/deploy/workspace/verify-browser.mjs" "$@"
        ;;
    aws-sim)
        if [[ "$#" -gt 2 || ( "$#" == 2 && "$2" != --check-persistence ) ]]; then exit 2; fi
        unset AWS_ACCESS_KEY_ID AWS_SECRET_ACCESS_KEY AWS_SESSION_TOKEN AWS_PROFILE AWS_DEFAULT_PROFILE AWS_WEB_IDENTITY_TOKEN_FILE
        unset AWS_CONTAINER_CREDENTIALS_RELATIVE_URI AWS_CONTAINER_CREDENTIALS_FULL_URI AWS_CONTAINER_AUTHORIZATION_TOKEN
        export AWS_EC2_METADATA_DISABLED=true AWS_CONFIG_FILE=/dev/null AWS_SHARED_CREDENTIALS_FILE=/dev/null
        "$root/deploy/workspace/aws/verify-terraform.sh"
        dotnet restore "$root/runtime/RuntimeKnowledge.slnx" --locked-mode
        exec "$root/deploy/workspace/database/test-db.sh" run dotnet test "$root/runtime/RuntimeKnowledge.slnx" --no-restore \
            --filter 'FullyQualifiedName~AwsSimulationTests|FullyQualifiedName~WorkspaceScenarioTests'
        ;;
    *)
        printf 'Usage: %s mac|home-lab|aws-sim [--check-persistence]\n' "$0" >&2
        exit 2
        ;;
esac
