# 0009 — Use a dedicated read-only source App per division and discover grants by polling

- **Status:** accepted
- **Date:** 2026-11-10
- **Feature:** #6 (docs/features/one-action-source-onboarding/)

## Context

Behaviour B1 makes a source owner's one action "add the repository to the division's App". B4 requires that grant to reach exactly one division, B5 requires an organisation-wide installation to admit only configured repositories, and B14 requires removal to be noticed. The vision observes that the current single sync App holds Contents **write** across every repository in its installation, so adding a source to it would not be a read-only grant. U11 asks whether a new grant is found by installation events, by polling, or both, and it must be visible after the next normal sync.

The catalog sync already runs every two hours and on demand (`.github/workflows/sync-catalog.yml`). GitHub Actions cannot receive a GitHub App webhook directly, so reacting to installation events would need a hosted receiver holding the App's webhook secret and some credential able to dispatch the workflow.

## Decision

Each division has its own **source-read App**, separate from its **division-writer App**:

- The source-read App has repository permissions Contents: read and Metadata: read, and nothing else. It is the App source owners add their repositories to. It is never installed on the division repository.
- The division-writer App keeps Contents and Pull requests write and is installed only on the division repository.
- Grants are discovered by **polling**. During the normal sync the importer lists the source-read App's installation repositories (`GET /installation/repositories`, followed to the last page) and treats that list as the complete set of grants. No webhook receiver is built.
- A listing that errors or is not read to the last page fails the sync and leaves `dist/` untouched. A partial listing is never read as "access removed".
- Repositories already listed in `sources.json`, and the division repository itself, are excluded from discovery, so a steward-registered private source that is also installed on the source-read App is one source (U7) with no import status.
- A division with no source-read App configured keeps working with steward-registered sources only. If its committed catalog already holds a granted source, the sync fails with a configuration message instead of treating the missing App as removed access.

## Consequences

- Making the owner action read-only is possible because the App source owners touch cannot write anywhere. Setting up a division now needs two Apps, which `docs/automation-setup.md` must say, with the single-App option withdrawn for divisions that want grants.
- A new grant appears after the next sync, so up to two hours later unless the sync is dispatched. Vision outcome 2 measures this against "the next successful normal sync", so no event path is needed.
- No new service, secret store or public endpoint is introduced. The only new credentials are the source-read App's client ID variable and private key secret.
- Each division installs its own App, so B4 holds by construction: another division's listing is a different installation.
- The merge-policy workflow already mints a source-read token for generated refreshes. It must mint it from the source-read App so its fresh build sees the same grants.
- The installation's `repository_selection` (`all` or `selected`) decides whether a repository without configuration is reported (`selected`) or ignored (`all`), per U5. The developer must confirm how to read it with the token-minting action in use.
- Renamed, transferred or archived repositories stay out of scope (U13).

## Alternatives considered

- **Reuse the existing single sync App.** Fewest administrative steps, but the owner would grant an App with write permission, which contradicts the vision's read-only consent.
- **Installation webhooks that dispatch the sync.** Faster than two hours, but it needs a hosted receiver and a dispatch credential. The vision asks only for the next normal sync.
- **Webhooks and polling together.** Adds the receiver and still needs polling to recover missed events. The extra speed is not required by any behaviour.
- **A per-source personal access token.** Moves the credential to individuals and cannot be scoped to one division.

## Amendment, 2026-11-10

Deployed acceptance found that the sync workflow's finalize step fetched the division repository with the source-read token, which this decision keeps off that repository, so a private division answered "Repository not found". The source-read token is for reading source repositories only: it clones sources and lists the installation, and it never authenticates git or the API against the division repository. Every step that touches the division repository uses the division-writer token or the workflow's built-in token. `test/workflow.test.mjs` asserts this for the sync workflow.
