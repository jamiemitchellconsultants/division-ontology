# Division ontology

A starter repository for a division to steward its shared vocabulary and publish a catalog of ontology concepts from its own participating repositories. The template starts with no registered sources or source links. The shared core includes the existing `StaffId` term.

The shared vocabulary lives in [`core/ontology.ttl`](core/ontology.ttl). Each source remains authoritative for its local concepts. [`sources.json`](sources.json) names participating repositories; `links/` holds explicit relationships to shared terms. The generated [`dist/catalog.md`](dist/catalog.md) is the readable view. [`dist/catalog.json`](dist/catalog.json) is a structured catalog; [`dist/combined.nq`](dist/combined.nq) preserves every source triple in named graphs. Both record source provenance.

Register your division’s source repositories in `sources.json` through the review process below.

## Start a new division from this template

Create the repository with **Use this template**, then run this once in a checkout of the new repository, before registering any sources:

```bash
npm ci
npm run init-division -- --namespace https://example.com/sales-division/ontology# --steward your-login --steward deputy-login
```

`init-division` also removes the template's own tests, `test/template.test.mjs` and `test/init-division.test.mjs`. They assert that the repository has no sources, so they would fail your required `test` check as soon as the first source joins. If you made a division before this change, delete those two files in a pull request.

It writes the namespace and steward logins to [`division.json`](division.json), moves the shared core into that namespace, sets the stewards as code owners, empties the Project Narrative and removes this template's in-flight feature work and planning history. It then rebuilds `dist/`. The stewards in `division.json` are the only reviewers whose approval satisfies the `merge-policy` check. Keep the template's namespace only if every division should share one set of core IRIs. Review and commit the result, then complete the [one-time automation setup](docs/automation-setup.md). Concepts with similar names remain separate unless a reviewed link relates them. Source commits in the generated catalog identify the published snapshot.

## Minimal instructions for an agent

- **Join `owner/repo` to division ontology** — for a source owner: the agent writes `.division-ontology.json` (ontology path and namespace) in the source repository and asks its administrator to add the repository to the division's read-only source App. Nothing is edited here and no steward approval is needed; the next sync imports it and shows its status.
- **Add `owner/repo` to division ontology** — the agent checks that repository's committed Turtle ontology, proposes its registration for steward review, and scheduled sync adds all its concepts after approval.
- **Sync division ontology now** — the agent triggers the manual sync workflow and reports its result.

The normal sync runs every two hours and maintains one generated catalog PR. Verified changes to unlinked source concepts can merge automatically after the [one-time setup](docs/automation-setup.md). Shared meaning and existing linked-concept changes still require a division steward.

## See it with sample data

The template starts empty. To see what ingesting repositories produces, use three synthetic repositories (Ecommerce, Warehouse and Accounting) with committed ontologies and source knowledge graphs. Everything is created outside this checkout; `sources.json`, the registries and `dist/` are not touched. The content is invented and is not evidence about any real system.

Catalog and C4 commands only (Node.js 20+, Git):

```bash
npm ci
npm run demo:setup      # creates the repositories and a division catalog in $TMPDIR/division-demo
npm run demo:teardown   # deletes that directory, including anything saved in it
```

Set `DEMO_DIR` to an absolute path to use another location. See [the fixture guide](test/fixtures/representative/README.md) for commands to run against it.

Workspace UI (Docker and jq as well):

```bash
npm run demo:up       # starts the workspace and publishes the sample snapshot
npm run demo:status   # shows the URL and where the sign-in passwords are
npm run demo:down     # removes the containers, data volumes and generated passwords
```

`demo:up` prints the address (default `http://workspace.localhost:8087`; set `WORKSPACE_PORT` to change it) and the file holding the generated passwords for `workspace-manager` and `workspace-reader`. It stays running until `demo:down`.

The demo builds its own division repository, so it skips the real registration step: a reviewed `sources.json` entry followed by the scheduled sync and catalog pull request. See [Contribute](#contribute) for that path.

## Build from a local checkout

Requires Node.js 20 or newer and a clean checkout of each source repository.

If the committed catalog holds granted sources, `build` and `check` also list the source-read App's installation, so run them with `DIVISION_SOURCE_APP=enabled` and `GH_SOURCE_TOKEN` set to a read-only installation token for that App (never in a file or argument list). Without them the command stops rather than treating the granted sources as removed.

```bash
npm ci
npm test
npm run build
npm run check
```

`build` writes the three files in `dist/` after all sources and links validate. `check` compares them against the current source commits without writing. A failed build leaves the previous complete catalog in place. With no sources registered, both commands use only the shared core and need no source checkouts. After registration, pass `--checkout owner/repo=/absolute/path` to both commands for each source you want to read locally; otherwise they clone the configured sources with your existing Git credentials. A local checkout must be clean and have an `origin` matching its configured source URL. The ontology is read directly from the recorded commit.

## C4 impact pilot

Run `npm run --silent acceptance:c4` after `npm ci` to exercise the pilot with synthetic sources. The runner creates its own fixtures and runs the repository tests and C4 acceptance checks. See the [C4 acceptance instructions](docs/features/c4-diagrams-from-source-knowledge-graphs/05-acceptance.md#automated-reruns).

The local pilot reads a published catalog commit, reviewed mapping and binding commit, and clean checkouts for every catalog source. Its JSON and Markdown reports retain evidence and gaps; they do not decide impact or approve a request. Graph-backed paths require a source graph at the catalog-pinned commit. The template’s mapping and binding registries start empty.

Use Node.js 20 or newer. Supply `--catalog-commit`, `--registry-commit`, an absolute `--checkout owner/repo=/path` for every source, and `--output /path/outside/repositories` to each command. For example:

```bash
npm run c4 -- search --catalog-commit <sha> --registry-commit <sha> --checkout owner/repo=/absolute/checkout --output /absolute/output --query "change description"
npm run c4 -- paths --catalog-commit <sha> --registry-commit <sha> --checkout owner/repo=/absolute/checkout --output /absolute/output --start owner/repo=https://example.org/node
npm run c4 -- view --catalog-commit <sha> --registry-commit <sha> --checkout owner/repo=/absolute/checkout --output /absolute/output --focus owner/repo=https://example.org/node --level context
```

View levels are `context`, `container`, `component` and `code`. `assessment-init` takes `--request-id` and `--description-file`; edit the generated JSON to record findings, then use `assessment-render --input FILE`. `feedback-render --input FILE --assessment-file FILE` renders a separate feedback record tied to the validated assessment digest. These commands also require the shared snapshot and checkout flags. To cite an inspected-code view in an assessment, include its digest in `viewIds` and supply its saved JSON using repeatable `--view-input FILE` on assessment and feedback rendering. `view` accepts an optional `--supplement FILE` for architect-inspected code evidence.

## Features and project narrative

- [`docs/features/`](docs/features/) holds each feature's working record: vision, behaviours, architecture, plans, and user acceptance.
- [`Narrative.md`](Narrative.md) is the generated project history of what was asked, decided, why, and what followed. Edit its fragments in [`narrative/entries/`](narrative/entries/), not the generated file.

## Try the workspace with disposable data

The workspace uses embeddings for semantic search. See [Configure embedding search](docs/adding-embedding-search.md) to choose a provider and connect your own deployment.

On a Mac with Docker and jq, install the browser used by the smoke test, then run it from a checkout:

```bash
npm ci
npx playwright install chromium
bash deploy/workspace/local/smoke.sh
```

The script builds a temporary catalog and snapshot from the synthetic Ecommerce, Warehouse, and Accounting repositories used by the C4 pilot. It starts an isolated Docker Compose project, publishes the snapshot, signs in through the UI, and checks that a saved annotation and cross-repository relationship survive a database restart and a rebuild from the journal. It removes the test containers and data volumes when it finishes. Set `WORKSPACE_PORT` if the default test port (18087) is in use.

### Workspace API reference

Every deployment serves the workspace API's OpenAPI spec at `/api/openapi/v1.json` and an interactive docs UI at `/api/docs`. Both are open to anyone. Data calls from the docs UI need the same sign-in and roles as the web UI. The committed copy of the spec is `runtime/openapi/workspace-api.json`. Building the API rewrites it:

```bash
dotnet build runtime/RuntimeKnowledge.Api
```

Commit the regenerated file in the same pull request as the code change. CI's `api-contract` job fails if the committed spec is out of date or an operation is not fully described.

## Contribute

See [GOVERNANCE.md](GOVERNANCE.md). In brief:

1. Propose a division-wide concept by pull request to `core/ontology.ttl`, with a definition and evidence from at least one source repository. A division steward reviews its meaning and name.
2. Add a repository to `sources.json` to include **all** of its ontology concepts. Give it a stable namespace and a Turtle path. Add a links file only for relationships that have been reviewed.
3. Use `exact` only when the local and shared concepts have the same meaning and canonical label. It emits `owl:equivalentClass`. Use `specializes` for a narrower local class; it emits `rdfs:subClassOf`. Matching labels alone create no link.
4. Run the tests, build and check locally. Commit authored changes in a reviewed PR. The sync workflow proposes generated output in its own PR after that merges.

## Licence

This repository is source-available under the [PolyForm Noncommercial License 1.0.0](LICENSE).
You may use, change and share it for any noncommercial purpose. Commercial use needs a separate
licence from the copyright holder. Report security problems as described in
[SECURITY.md](SECURITY.md).

## Current boundary

The division build validates the shared core and produces a combined catalog. Source repositories remain authoritative for their own ontologies. Making participating repositories’ ontology checkers consume a pinned division-core snapshot needs a later Ontology Kit integration; division validation currently runs here when this catalog is built.

CI runs the code tests without cross-repository credentials. The scheduled sync uses a GitHub App to read private sources. Until the [App and branch-rule setup](docs/automation-setup.md) is complete, catalog refreshes remain manual and the existing owner review applies. This repository is public, and the catalog includes every concept and description from each registered source. Registering a private repository publishes its ontology here, so register only sources whose ontology may be public.
