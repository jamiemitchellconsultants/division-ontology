# One-action source onboarding — product vision

**Feature:** [#6](https://github.com/jamiemitchellconsultants/division-ontology/issues/6)

**Phase:** [#9 — Product owner vision](https://github.com/jamiemitchellconsultants/division-ontology/issues/9)

## Problem

A source repository owner wants their project's internal ontology to become visible in the division catalog. Today they must coordinate a source-registration pull request, division steward approval, and GitHub App access, even though the project has already passed corporate approval. The number of steps makes joining slow and discourages participation. The owner administers their own repository but has no write access to `division-ontology`.

## Users

- **Source repository owner:** opts their approved project into the division catalog and maintains the source ontology in their own repository.
- **Read-only App owner:** oversees imports, sees failures, and contacts the source owner when the source needs repair.
- **Division catalog reader:** discovers participating repositories and their imported concepts, with enough provenance to judge freshness.

## Outcomes and measures

1. **One owner action to join.** A source owner who administers their repository grants the preapproved read-only App access to it. Measure: no edit, pull request, or write permission in `division-ontology` is required from that owner, and no separate admission approval is needed.
2. **Prompt first visibility.** A valid new source appears in the catalog after the next successful normal sync. Measure: the catalog identifies the source repository and exact imported commit; the owner has taken no second onboarding action.
3. **Unattended updates.** New valid ontology versions are imported through routine sync. Measure: changing and committing the source ontology requires no renewed registration or permission grant.
4. **Actionable import status.** The App owner can tell which repositories have joined, which revision was last attempted and published, and why any attempt failed. Measure: a missing, invalid, or inaccessible source has a source-specific status; the last valid published catalog remains available, and one failed source does not hold back valid sources.

## Scope

- Source-owner consent to import all concepts in their committed internal ontology by granting a selected-repository, read-only GitHub App access to their repository.
- Automatic recognition of a newly granted source, validation of its ontology and repository-owned import metadata, and inclusion of valid versions in the division catalog.
- App-owner-visible import status for new sources and later versions, including failures and stale published versions.
- A clear status when a previously joined source's App access is removed, so the catalog does not imply that it is current.
- Onboarding guidance and admission governance that reflect the single owner action.

## Out of scope

- Automatic equivalence claims, cross-source links, or changes to division-owned shared terms; those continue through their own reviewed governance.
- Requiring complete C4 architecture coverage as a condition of joining. [Side issue #7](https://github.com/jamiemitchellconsultants/division-ontology/issues/7) preserves that separate request.
- Curating or selecting only part of a source ontology during onboarding.
- Publishing source concepts to readers beyond the division catalog's existing audience.

## Research

- [Current onboarding instructions](https://github.com/jamiemitchellconsultants/division-ontology/blob/main/docs/Add-Repo-With-Internal-Ontology-To-Division-Ontology-Three-Step-Instructions.md) split work across a division steward, a contributor, and a source owner. This is the coordination the feature removes for first admission.
- [Current automation setup](https://github.com/jamiemitchellconsultants/division-ontology/blob/main/docs/automation-setup.md) already uses a GitHub App to read private sources, but its single-App option has latent write permission on them. A dedicated source-read App is needed to make the chosen owner action genuinely read-only.
- [InternalOntology's installation guide](https://github.com/jamiemitchellconsultants/InternalOntology/blob/main/docs/ai-agent-install.md) produces a committed Turtle ontology and records the ontology path and namespace in repository-owned configuration. This gives the importer a source contract to validate rather than asking the owner to enter those fields in the division repository.
- [GitHub's App installation rules](https://docs.github.com/en/apps/using-github-apps/installing-a-github-app-from-a-third-party) allow a repository administrator to add a preapproved App to repositories they administer, subject to organization policy. The user confirmed source owners have that permission in this environment.
- [GitHub's installation-repository event](https://docs.github.com/en/webhooks/webhook-events-and-payloads) reports repositories added to or removed from an App installation. The analyst and architect can choose how discovery is triggered while preserving the one-action journey.
