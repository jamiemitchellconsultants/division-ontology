# Runtime knowledge workspace runbook

## Deploy

Acceptance uses a disposable Mac Docker stack and a separate synthetic fixture deployment on the LocalAI home lab. Use Node and the locked dependencies in the repository, Docker Compose, jq, the SDK pinned in `runtime/global.json`, and Playwright Chromium. From the repository root:

```bash
npm ci
npx playwright install chromium
evidence_root=$(mktemp -d)
bash deploy/workspace/acceptance.sh local "$evidence_root/local"
```

The local runner builds and activates the Ecommerce, Warehouse and Accounting fixture. The Compose project, credentials, ports and volumes are isolated. The script restarts the API and PostgreSQL, verifies a guarded replay, then removes its containers and volumes. Set `WORKSPACE_PORT` if port 18087 is occupied. Keep the evidence directory for the acceptance record.

For the home lab, the LocalAI operator installs the reviewed image revision and provisions HTTPS, Keycloak, PostgreSQL, MinIO and the Qwen3 8B Q4 embedding endpoint. Use the [deployment contract](../../../deploy/workspace/home-lab/deployment-contract.md) and [LocalAI installer guide](https://github.com/jamiemitchellconsultants/LocalAI/blob/main/docs/deploy-division-workspace.md). Record the five image digests, HTTPS origin and immutable model revision. Use a dedicated fixture installation and distinct persistent volumes for acceptance. The standard LocalAI installer uses fixed Compose project, volume and realm names, so a second installation using that installer needs a separate Docker and Keycloak environment.

Generate a synthetic bundle on the runner and transfer the bundle directory and printed manifest digest to the fixture operator:

```bash
fixture_root=$(mktemp -d)
node src/workspace/seed-fixture.mjs "$fixture_root/checkouts" "$fixture_root/bundle"
```

The operator uploads, stages and activates the same digest using the installer’s bundle options or the publisher commands below. Check that the migration completed, the API container’s internal `/health` reports `ready`, the indexer is healthy, and the browser can sign in and see the expected active snapshot.

## Operate

For an installer-managed fixture, use its working directory and Compose configuration. The defaults below match the LocalAI Windows installer. Set `$BundleDir` and `$ManifestId` to the verified fixture bundle and digest.

```powershell
$WorkspaceRoot = 'C:\mcp\division-workspace'
$ComposeArgs = @('compose', '-p', 'runtime-knowledge-workspace',
    '-f', "$WorkspaceRoot\src\deploy\workspace\home-lab\compose.yml",
    '--env-file', "$WorkspaceRoot\workspace.env")
docker @ComposeArgs ps -a
docker @ComposeArgs run --rm --no-deps -v "${BundleDir}:/bundle:ro" publisher upload /bundle
docker @ComposeArgs run --rm --no-deps publisher stage $ManifestId
docker @ComposeArgs run --rm --no-deps publisher activate $ManifestId
```

When activation reports `snapshotIndexPending`, wait for indexing and try activation again. If staging fails, inspect the publisher error and check the active snapshot before trying a corrected bundle. Record the manifest digest and published source commits. Check `$LASTEXITCODE` after each publisher or replay command before continuing.

For deployed acceptance, set `WORKSPACE_SCENARIO_DIR` to a private runner directory and write the active synthetic digest to its `snapshot` file. Supply the fixture HTTPS origin, app version, immutable model revision, and controlled manager and reader credentials through the runner environment using the variable names in the deployment contract. Keep the same scenario directory across these runs:

```bash
bash deploy/workspace/acceptance.sh home-lab "$evidence_root/home-lab-initial"
# The LocalAI operator restarts the fixture API and database, retaining their volumes.
bash deploy/workspace/acceptance.sh home-lab "$evidence_root/home-lab-restarted" --check-persistence
```

The first run records the contribution IDs in `scenario.json`. The persistence run looks them up and verifies traversal, history and indexed search. Record the operator, app revision, snapshot and model revision, along with the indexing wait and query latency logged by the browser. Check indexer logs for a provider or queue issue if search remains pending. Application-level checks that need replacement snapshots, additional identities or failure injection are listed in the [acceptance record](05-acceptance.md).

## Recover

To roll back a published baseline, stage the previous verified bundle digest and activate it when its index is ready. Confirm the active digest and inspect saved contributions and endpoint availability. To roll back the application release, have the operator select the previous image digests and confirm database migration compatibility before starting it with the existing volumes.

To rebuild the projection on the **fixture**, prefix each command below with `docker @ComposeArgs run --rm --no-deps replay`:

1. Run `writes pause`, then `describe-current`. Record the active snapshot, generation, journal position and state digest.
2. Run `replay --through POSITION`. Compare the rebuilt snapshot and state digest with the active values at the same position. A different generation with matching snapshot and state digest is the expected result.
3. Run `activate-replay --generation GENERATION --expect-position POSITION` only when the comparison passes. Confirm `describe-current` identifies the rebuilt generation. If the comparison fails, leave the active projection in service and investigate the evidence.
4. Run `writes resume` after the controlled operation, including after a failed comparison when the active projection is healthy. Repeat the deployed persistence run using a fresh evidence directory.

Keep the PostgreSQL journal volume and referenced MinIO bundles available for replay. Loss of either, or recovery when the active projection cannot be compared, belongs to [backup and disaster recovery #53](https://github.com/jamiemitchellconsultants/division-ontology/issues/53).
