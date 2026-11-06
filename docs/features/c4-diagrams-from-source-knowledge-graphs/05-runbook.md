# C4 impact pilot runbook

Acceptance environments: the primary Mac and a second Mac with a clean checkout. James Mitchell is the walkthrough participant, architect and reviewer.

## Deploy

The pilot is a local Node.js CLI, not a hosted service. Install Node.js 20 or later and Git. In a clean checkout of the reviewed division revision, run `npm ci` and `npm test`. Obtain a clean checkout of every source listed in the published `dist/catalog.json`; each checkout must have the configured `origin` and contain the recorded source commit. Preserve access to those commits. Do not store credentials in command arguments or files.

Choose the published division catalog commit and the reviewed mapping and binding commit. The latter contains `c4/mappings.json` and `c4/bindings.json`. Select an absolute output directory outside the division and source checkouts. Repeat `--checkout owner/repo=/absolute/path` for every source. Keep the catalog and registry commits, source paths, Node version and division revision with the pilot record. Production registries initially contain no mappings or bindings; a missing source graph is reported as a gap.

For deployed acceptance, use the second Mac. Check out the same reviewed division revision as the local run and record its machine name, project commit, Node version and generated fixture commits. Run the setup block in [the acceptance walkthrough](05-acceptance.md#start-the-walkthrough). It installs dependencies and builds the same synthetic sources locally. Record the test evidence as synthetic acceptance coverage.

## Operate

From the division checkout, pass the same snapshot arguments to each command:

```sh
npm run c4 -- search --catalog-commit <catalog-sha> --registry-commit <registry-sha> --checkout owner/repo=/absolute/source --output /absolute/output --query 'change description'
npm run c4 -- paths --catalog-commit <catalog-sha> --registry-commit <registry-sha> --checkout owner/repo=/absolute/source --output /absolute/output --start owner/repo=https://example.org/node
npm run c4 -- view --catalog-commit <catalog-sha> --registry-commit <registry-sha> --checkout owner/repo=/absolute/source --output /absolute/output --focus owner/repo=https://example.org/node --level context
```

Inspect command exit status and the resulting JSON or Markdown. A successful command reports the number of files written; an empty result can still contain coverage gaps. Confirm the recorded snapshot and review gaps, evidence and the source identities before using an output. Run `npm test` as a local health check. No service health endpoint exists.

## Recover

If a command fails, retain its error and verify the supplied commits exist, every checkout is clean and points to the configured origin, every source is present, and the output directory is outside all checkouts. Missing graph coverage is a knowledge gap, not proof of no impact. Restore an inaccessible pinned source commit or use a separately published snapshot; never substitute a newer commit silently. If mappings or bindings are invalid, return to the previously reviewed registry commit or seek a reviewed correction. To roll back the CLI, use a separate clean checkout of the previous reviewed division revision and reinstall its dependencies; retain the original catalog and registry commits and output for audit. Rerun the health check and reproduce the report with its original inputs.
