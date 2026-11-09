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

### Home-lab acceptance

1. **Choose an isolated fixture installation.** The LocalAI operator needs Docker Compose, Caddy, Keycloak, working HTTPS DNS and the Qwen3 8B Q4 embedding service. Use distinct persistent volumes for acceptance. The standard LocalAI installer uses fixed Compose project, volume and realm names, so a second installation using that installer needs a separate Docker and Keycloak environment. Follow the [deployment contract](../../../deploy/workspace/home-lab/deployment-contract.md) and [LocalAI installer guide](https://github.com/jamiemitchellconsultants/LocalAI/blob/main/docs/deploy-division-workspace.md).
2. **Select the release.** Use a reviewed branch or tag whose five workspace images were published. If the selected commit has no images, run **Publish workspace images** for that revision before installing. Obtain the actual GGUF SHA-256 from the Qwen service operator and supply it to the installer as `-EmbeddingRevision`. The service must return 4,096 dimensions. Provide the installer's GitHub access through its configured credential store.
3. **Build the fixture.** On the fixture host, open a checkout of the acceptance branch **outside** the installer’s working directory. The installer manages its own `src` checkout. In PowerShell from the acceptance checkout, run:

   ```powershell
   npm ci
   npx playwright install chromium
   $FixtureRoot = Join-Path ([IO.Path]::GetTempPath()) ("workspace-fixture-" + [guid]::NewGuid())
   New-Item -ItemType Directory -Path $FixtureRoot | Out-Null
   $BundleDir = Join-Path $FixtureRoot 'bundle'
   $ManifestId = node src/workspace/seed-fixture.mjs (Join-Path $FixtureRoot 'checkouts') $BundleDir
   if ($LASTEXITCODE -ne 0 -or $ManifestId -notmatch '^[a-f0-9]{64}$') { throw 'Fixture build failed.' }
   ```

4. **Install and publish.** From Administrator PowerShell, run the reviewed LocalAI installer. Set `$Installer` to its script path, `$FixtureHost` to the dedicated HTTPS hostname without the scheme, `$ReleaseRef` to the reviewed Git ref, and `$ModelDigest` to the digest from step 2. Preserve the deployment evidence it writes.

   ```powershell
   & $Installer -PublicHost $FixtureHost -GitRef $ReleaseRef -EmbeddingRevision $ModelDigest `
       -BundleDir $BundleDir -ManifestId $ManifestId
   if ($LASTEXITCODE -ne 0) { throw 'Fixture installation failed.' }
   ```

5. **Supply fixture identities.** Configure separate controlled manager and reader accounts in the fixture realm. Use the runner’s secret store to set `WORKSPACE_VERIFY_MANAGER_USERNAME`, `WORKSPACE_VERIFY_MANAGER_PASSWORD`, `WORKSPACE_VERIFY_READER_USERNAME` and `WORKSPACE_VERIFY_READER_PASSWORD` in its process environment. The runner reads the hostname, application revision, image digests and model revision from `deployment-evidence.json`. Run it where both the fixture Docker project and the HTTPS origin are reachable. Use the fixture’s trusted DNS and TLS configuration.
6. **Run the deployed walkthrough.** From the acceptance checkout, set `$Operator` to the name to record in the evidence. Run the command below with the manifest digest from step 3 and a new evidence path under a private directory.

   ```powershell
   $Deployment = 'C:\mcp\division-workspace\deployment-evidence.json'
   $Evidence = Join-Path $FixtureRoot 'acceptance-evidence'
   node deploy/workspace/acceptance-home-lab.mjs --deployment $Deployment `
       --snapshot $ManifestId --evidence $Evidence --operator $Operator
   if ($LASTEXITCODE -ne 0) { throw 'Review the reported acceptance exception.' }
   ```

   The runner checks the configured and running image digests, service health and active fixture digest before making any test writes. It runs the browser checks, restarts only PostgreSQL and the API, checks persistence, performs guarded replay and checks persistence again. It resumes writes after a replay failure. `run.json` lists the steps and their outcomes. Browser logs record the pilot searches, index wait and query latency. The projection JSON files record the compared digests and journal positions.
7. **Review exceptions.** Keep the evidence directory and record its results in [the acceptance record](05-acceptance.md). Check the remaining host scenarios in that record, including replacement snapshots, role changes and explicit codification. Record B14 separately when an independent reviewer is available.

The runner derives the Compose path and environment file from the installer’s evidence directory. Use `--compose`, `--env-file` and `--project` for an explicitly configured fixture project with different paths or name. If a runner is stopped while writes are paused, confirm the active fixture and follow the recovery steps below to resume them. Use a fresh evidence directory for each run.

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

For browser-only troubleshooting from another machine, set `WORKSPACE_SCENARIO_DIR` to a private runner directory and write the active synthetic digest to its `snapshot` file. Supply the fixture HTTPS origin, app version, immutable model revision, and controlled manager and reader credentials through the runner environment using the variable names in the deployment contract. Keep the same scenario directory across these runs:

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
