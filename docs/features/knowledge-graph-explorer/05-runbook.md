# Knowledge Graph Explorer runbook

[Feature #145](https://github.com/jamiemitchellconsultants/division-ontology/issues/145) · [User phase #151](https://github.com/jamiemitchellconsultants/division-ontology/issues/151) · [Acceptance walkthrough](05-acceptance.md)

## Deploy

Acceptance uses the disposable Mac workspace first and the shared home-lab workspace when the operator has assigned it to the synthetic Order Fulfilment fixture. Use the release that contains [build PR #171](https://github.com/jamiemitchellconsultants/division-ontology/pull/171) or a later reviewed release. Record the deployed application revision and the active snapshot before starting.

### Mac walkthrough

From the repository root, install the locked dependencies and Chromium if needed. Use a new scenario directory and a free loopback port. These commands retain the exact Turtle checkouts used for B15.

```bash
npm ci
node node_modules/playwright/cli.js install chromium
export WORKSPACE_SCENARIO_DIR="$(mktemp -d)"
export WORKSPACE_DEMO_DIR="$WORKSPACE_SCENARIO_DIR/demo"
export WORKSPACE_PORT=18145
npm run demo:up

set -a
source "$WORKSPACE_DEMO_DIR/demo.env"
set +a
manifest="$(node src/workspace/seed-fixture.mjs "$WORKSPACE_SCENARIO_DIR/checkouts" "$WORKSPACE_SCENARIO_DIR/bundle")"
bash deploy/workspace/local/bootstrap.sh "$WORKSPACE_SCENARIO_DIR/bundle" "$manifest"
```

The demo creates its first fixture as part of startup. The second activation supplies the retained checkouts for the walkthrough. Open `http://workspace.localhost:18145`. Use separate browser profiles for `workspace-reader` and `workspace-manager`. The generated `demo.env` contains the two fixture passwords.

### Home lab

Use the shared workspace installation and pin all images to the reviewed release. Follow the existing install and fixture publication steps, including keeping the PostgreSQL and MinIO volumes. Select the intended synthetic bundle and retain its manifest and clean source and adopting checkouts. Wait for baseline indexing and confirm the expected active digest. [Workspace deployment contract](../../../deploy/workspace/home-lab/deployment-contract.md) · [Workspace publication and recovery](../runtime-knowledge-workspace/05-runbook.md#operate)

Assign `ReadKnowledgeGraph` to the fixture reader and `ManageKnowledgeGraph` to the separate fixture manager. Provide the two sets of credentials through the runner environment. Use a third controlled manager account or a second manager browser context for the version-conflict walkthrough. Keep fixture passwords and access tokens out of evidence.

To enable GitHub evidence links, set `WORKSPACE_EVIDENCE_REPOSITORIES` to a JSON mapping from source IDs to HTTPS GitHub repository roots. Use the same Compose env file and process overrides that will be deployed.

```bash
bash deploy/workspace/home-lab/verify-config.sh --env-file /path/to/workspace.env
```

For example, `{"fixture/ecommerce":"https://github.com/fixture/ecommerce"}` configures the Ecommerce source. Links pin the citation to its recorded commit and path. With the map unset, users see the plain citation and the evidence-link status. AWS uses the corresponding Terraform `evidence_repositories` map. The home lab is the deployed acceptance target for this phase.

## Operate

- Confirm that search shows the intended active snapshot and that both fixture accounts can open **Open graph** from the Ecommerce **Order** entity. Check the plain citation or commit-pinned evidence link according to the environment's configuration.
- From the home-lab operator terminal, check the API and session using the shared workspace health steps. Graph assets are served from the workspace origin. The initial graph module and the pinned Cytoscape module both need a JavaScript content type. [Workspace health checks](../runtime-knowledge-workspace/05-runbook.md#operate)
- Run the Graph Explorer regression suite below after walkthrough and executable-test approval, with the home lab assigned to fixture acceptance. Use the shared workspace smoke separately to verify the broader workspace and persistence.
- On Mac, use `npm run demo:status` to check the walkthrough workspace. Leave the retained input checkouts available until the before-and-after evidence has been recorded. Once the disposable walkthrough is done, `npm run demo:down` tears down its containers and volumes.

### Graph Explorer regression on ai-mcp-server

From the updated repository checkout on Windows host `ai-mcp-server`, run this PowerShell fragment against `https://division.tqaentry.com`. Set the three pins at the top to the retained fixture run directory, the expected installed application commit and the active synthetic snapshot. Run `npm ci` and `node node_modules/playwright/cli.js install chromium` in the checkout if the locked dependencies or browser are missing.

```powershell
$FixtureRunRoot = '<retained fixture run directory>'
$ExpectedRevision = '<40-character installed commit>'
$Snapshot = '<64-character active fixture digest>'
& ./deploy/workspace/run-home-lab-graph-regression.ps1 `
    -FixtureRunRoot $FixtureRunRoot -ExpectedRevision $ExpectedRevision -Snapshot $Snapshot -AllowFixtureWrites
```

The default run covers B1–B15. It checks the deployment evidence, retained manifest and live snapshot before creating records. Playwright uses local Caddy ingress and opens visible Chromium for the Windows keyboard case. It reuses the fixture account passwords from the current PowerShell process and prompts with masked input when a password is missing or Keycloak rejects it at preflight. The reader and manager accounts must already be set up in Keycloak.

For a focused run, pass `-Behaviour B8` or a comma-separated list such as `-Behaviour B7,B12`. B13 runs B12 to create its records. B15 runs B12 and B13 to produce the journal evidence it checks. B1, B2, B6 and B9 can run with reader credentials alone. The remaining cases require `-AllowFixtureWrites`.

Each run prints the test, expected result and actual result. The new `graph-regression-*` directory under the retained fixture run root contains `run.json`, before and after input captures, and the exact contribution IDs and revisions in `records.jsonl`. The runner removes its temporary records on both pass and handled failure. If cleanup is incomplete, use the unconfirmed IDs in the report and inspect their live histories before removing any remaining records. The records' journal history remains available for auditing.

## Recover

| Symptom | Action |
| --- | --- |
| Diagram reports startup failure | Choose **Retry diagram**. Use the neighbour list while checking the bridge and vendor module requests at the workspace origin. Inspect the HTTP status, content type, and browser error if both attempts fail. |
| Results are out of date | Choose **Reload** at the same focus and direction. On a transient request failure, use **Retry**. |
| Save succeeded and the next read failed | Keep the displayed **Change saved** confirmation. Use **Retry** for the read or open the saved record or its history. |
| Save was rejected | Keep the editor in the same browser tab. Review a conflicting revision before retrying. Use **Sign in again** for an expired session. Use the published endpoint choices to resolve an unavailable endpoint, or ask the workspace operator to check the assigned role after a denial. |
| Evidence configuration fails preflight | Correct or unset the map and rerun preflight with the deploy environment. Check that the app settings contain the expected identity and reload the web service after deploying the corrected settings. |

For release rollback, confirm the previous image set is compatible with the installed database, then rerun the shared installer with that reviewed release and the existing volumes. For a published-data rollback, verify and activate the previous known bundle through the publisher. Check health and the active digest after either action. [Workspace recovery commands](../runtime-knowledge-workspace/05-runbook.md#recover)
