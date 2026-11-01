# One-action source onboarding — design draft

**Issue:** [#6](https://github.com/jamiemitchellconsultants/division-ontology/issues/6)

**Status:** For product review

## Intent

A source repository owner should be able to join the division ontology with one deliberate action. They already have corporate approval to run the project, administer their own GitHub repository, and have no write access to `division-ontology`. The current onboarding asks several people to coordinate an App grant, a source-registration pull request, and steward approval. That effort discourages participation and delays visibility of a source's concepts.

## Goals

- The source owner grants a preapproved, selected-repository GitHub App read access to their repository. That grant is the whole human onboarding action.
- A valid source appears in the division catalog after the next successful sync, without a hand-edited `sources.json` entry or a registration pull request.
- Later committed ontology versions flow through the ordinary catalog refresh process.
- The App owner can see which repositories were discovered, which commit was imported, and a specific reason for any failed import. They communicate directly with the source owner when repair is needed.
- A failed new source does not appear as successfully joined or prevent updates from already valid sources. A previously published source keeps its last valid version until the new version passes validation.

## Scope

- Discover repositories newly granted to a source-read GitHub App, and withdraw them when that grant is removed.
- Read each source's committed Turtle ontology and enough repository-owned metadata to identify its path, namespace, and default ref. Repositories installed with InternalOntology already publish Turtle and record the ontology path and namespace in `ontology.config.json`.
- Validate source identity, access, ontology content, namespace, and provenance before publication.
- Keep an App-owner-facing import status showing the source, commit, last attempt, result, and actionable failure reason.
- Update the source onboarding instructions and governance rule so source-owner consent admits a source without a division-steward approval step.

## Out of scope

- Automatically asserting equivalence between source concepts or creating cross-source links.
- Changing the approval path for division-owned shared terms or reviewed links.
- Requiring C4 architecture coverage before a repository can join. [Issue #7](https://github.com/jamiemitchellconsultants/division-ontology/issues/7) tracks that separately.
- Publishing source ontology content outside the existing division catalog readership.

## Approaches considered

| Approach | Owner action | Tradeoff |
| --- | --- | --- |
| Selected-repository, read-only GitHub App **(chosen)** | Grant the preapproved App access to their repository | Reuses GitHub's repository-scoped consent and the current pull-based sync. The division side must discover grants and derive source metadata automatically. |
| Source-side publish to shared storage | Enable and configure a publishing workflow | Avoids source-repository read access for division sync, but introduces storage, workflow setup, and publishing credentials per source. |
| Per-repository deploy key | Add a division-provided public key as a read-only deploy key | Gives repository-specific read access, but makes every onboarding a key exchange with a long-lived credential to maintain. |

The App is read-only on selected source repositories. Division write capability belongs to a separate credential scoped to `division-ontology`; the existing single-App setup has latent source-write capability and must be separated before this flow is offered.

## Proposed owner journey

1. The source owner grants the preapproved source-read App access to their repository.
2. The next sync discovers the grant, derives the source's import metadata, validates the committed ontology, and publishes a catalog refresh through the existing trusted path.
3. The App owner reads the import status. On failure, it names the source and the repair needed; the App owner contacts the source owner.
4. Future valid commits are ingested without repeating onboarding.

Revoking App access stops future reads. The design phase should specify how the catalog marks or removes a previously imported source so it does not imply the source is still current.

## Constraints and risks

- The App grant is consent to include **all** concepts in the source ontology under the existing catalog's readership. The owner is not asked to curate a subset.
- A source with no deterministic committed Turtle ontology, or with missing path and namespace metadata, is reported as incomplete rather than guessed into the catalog.
- Source failures must be isolated; the last valid catalog remains usable. Status must distinguish a never-imported source from a previously imported source whose latest revision failed.
- GitHub organization policy must permit a source repository admin to add the preapproved App. The user has confirmed that source owners have this permission in the intended environment.
- Generated output remains traceable to the exact source commit, and no source code or workflow from a source repository is executed with division credentials.

## Acceptance checks

1. An owner with source-repository admin rights and no division write access grants App access once; a valid source appears in the catalog after sync with its commit recorded.
2. A subsequent valid source ontology change updates the catalog without another owner action.
3. A missing or invalid ontology yields a source-specific, App-owner-visible failure and no false success state; other valid sources still refresh.
4. Removing App access ends future ingestion and makes the source's published freshness state explicit.
5. Neither source access nor division write credentials are stored in source files, generated catalog files, pull-request bodies, or logs.

## Builder decisions still open

Choose polling or GitHub App repository events for discovery; the durable representation of discovered versus active sources; the exact App-owner status surface; and the transition for existing manually registered sources. These choices must preserve the owner journey and acceptance checks above.

## Sources

- [Current onboarding and governance](../../../AGENTS.md)
- [Current one-time automation setup](../../../docs/automation-setup.md)
- [GitHub App installation rules](https://docs.github.com/en/apps/using-github-apps/installing-a-github-app-from-a-third-party)
- [GitHub App installation repository events](https://docs.github.com/en/webhooks/webhook-events-and-payloads)
- [GitHub App installation token scoping](https://docs.github.com/en/apps/creating-github-apps/authenticating-with-a-github-app/generating-an-installation-access-token-for-a-github-app)
