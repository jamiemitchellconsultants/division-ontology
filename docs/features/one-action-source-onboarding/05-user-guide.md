# One-action source onboarding user guide

**Feature:** [#6](https://github.com/jamiemitchellconsultants/division-ontology/issues/6)

Written for the three users in the [vision](01-vision.md): a **source repository owner**, the **read-only App owner** and a **division catalog reader**. Deployment and operation are in the [runbook](05-runbook.md); the setup of the Apps is in [docs/automation-setup.md](../../automation-setup.md).

## For a source repository owner

### Join a division (B1, B4, B6)

1. Commit `.division-ontology.json` at the root of your repository's default branch:

   ```json
   { "schemaVersion": 1, "ontologyPath": "docs/ontology.ttl", "namespace": "https://example.org/ontology#" }
   ```

   `ontologyPath` is the committed Turtle ontology; `namespace` is the one its concepts use. No other keys are allowed, and the file must be under 16 KiB.
2. Add your repository to the division's source-read App (its selected repositories). You need administrator access to your repository and your organisation must let you add the App. The repository must be in the same GitHub account or organisation as the division.
3. Wait for the next sync, up to two hours, or ask the App owner to dispatch it. Nothing is edited in the division repository and no steward approves your repository.
4. Find your repository's row under **Import status** in the division's `dist/catalog.md`. `current` means every concept in your ontology is published at the commit shown. You join one division only; granting another division's App is a separate action (B4).

### Keep it up to date (B8, B9)

Commit to your default branch as usual. The next sync publishes the new commit and its concepts with no renewed grant. If you move the ontology or change the namespace, commit the new `.division-ontology.json` with it; the next sync follows it.

### Fix a failed import (B11, B12, B13, B16)

Read your row's status and reason code:

| Status and code | What to do |
| --- | --- |
| `not configured`, `config-missing` | Commit `.division-ontology.json`, or remove the repository from the App. |
| `invalid`, `config-invalid` | Fix the file: only `schemaVersion` 1, `ontologyPath` and `namespace`, and the path must stay inside the repository. |
| `invalid`, `ontology-unreadable` | `ontologyPath` must name a committed regular file (not a symbolic link) of 32 MiB or less. |
| `invalid`, `ontology-invalid-turtle` | The detail gives the line to fix. |
| `invalid`, `concept-outside-namespace` | A concept, or a statement, names an IRI outside your namespace. Your ontology may only describe its own terms. |
| `invalid`, `no-concepts` | The ontology defines no concepts in the namespace. |
| `invalid`, `namespace-conflict` | Your namespace overlaps the division core's or another source's. Choose another. |

While a later commit is invalid, the last valid version stays in the catalog marked stale. Commit a fix and the next sync returns you to `current`. A source that was never valid is reported but none of its concepts are published (B12).

### Leave, or lose access (B14)

Removing your repository from the App leaves its last published version in the catalog, marked stale with status `access removed`. Adding it back resumes normal imports. A repository that was never published simply leaves the status table.

### What joining does not do

A granted source cannot propose links to shared terms, change shared terms, or claim a namespace already in use (B2, B16). Those stay with the division stewards.

## For the read-only App owner

### See what has joined (B10)

Every joined repository has a row in **Import status** with its status, last attempted commit, last published commit and, if it failed, a coded reason you can act on. Reasons contain no credentials. The refresh PR body repeats the table. One failing repository never holds back the others (B15).

### Watch refreshes merge (B2, B17)

A refresh that only adds, updates or changes the status of granted sources is classified automatic and merges without a steward. If nothing changed since the last published catalog, the sync opens no PR (B17). Refreshes that touch links, the core, `sources.json` or `division.json` still need a steward.

### A repository that is also registered (B3)

A repository listed in `sources.json` and also added to the App appears once, as `registered`, with no import status row. Its steward registration governs its path, namespace and ref.

### Start a new division (B7)

A division created from the template with `node scripts/init-division.mjs ...` starts with no sources and an empty import status table. The template itself never holds sources.

## For a catalog reader

### Judge freshness (B10)

Open **Import status** in `dist/catalog.md`, or `importStatus` and each source's `origin` and `stale` in `dist/catalog.json`. A source is current only when its status is `current`. If it is `invalid` or `access removed`, the concepts shown are from the last published commit and may be out of date. The repository and commit identify exactly what was imported.

### All repositories (B5)

If the App is installed on all of an account's repositories, only repositories that carry `.division-ontology.json` appear. Others are not listed at all.
