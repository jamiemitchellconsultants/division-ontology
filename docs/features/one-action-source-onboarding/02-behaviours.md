# One-action source onboarding — behaviours

**Feature:** [#6](https://github.com/jamiemitchellconsultants/division-ontology/issues/6)

**Phase:** [#10 — Analyst behaviours](https://github.com/jamiemitchellconsultants/division-ontology/issues/10)

**Basis:** [Accepted product vision](01-vision.md) as amended in PR #77, both product-owner handoffs on #6, and the analyst decisions recorded with the user. Each numbered outcome below matches the vision. Scenarios describe what can be observed; they do not prescribe components, triggers or deployment.

"Division" means one catalog repository created from the template, with its own `division.json` stewards and its own read-only App. "Normal sync" means the division's scheduled or manually dispatched catalog sync and the catalog refresh it produces.

## Behaviours

### Outcome 1 — One owner action to join

#### B1 — A SourceAccessGrant registers the source

**Given** a division with its own read-only App, and a repository whose default branch has a committed SourceImportConfiguration and a valid Turtle ontology, **when** an administrator of that repository adds it to the division App's selected repositories, **then** the next normal sync treats the repository as a joined source. The owner makes no edit, pull request or write in the division's catalog repository, and `sources.json` and `division.json` are unchanged.

#### B2 — Granted-source refreshes merge without a steward

**Given** a catalog refresh whose only changes add, update or change the SourceImportStatus of granted sources, **when** the refresh is classified, **then** it is automatic and merges without steward approval. **Given** a refresh that also changes a reviewed link, a linked concept or the division core, **when** it is classified, **then** it requires steward review as it does today.

#### B3 — Steward registration continues alongside grants

**Given** a repository registered in `sources.json`, **when** it is changed or synced, **then** it still needs a current steward approval to register and is imported as today. **Given** a repository both registered in `sources.json` and granted to the App, **when** sync runs, **then** it appears once in the catalog, and its steward registration governs the ontology path, namespace and ref.

#### B4 — A grant joins exactly one division

**Given** two divisions created from the template, each with its own App, **when** a source owner grants division A's App access, **then** the source appears in division A's catalog only, and division B's catalog and status are unchanged.

#### B5 — An all-repositories installation admits only configured repositories

**Given** a division App installed on an organisation with "All repositories", **when** sync runs, **then** each readable repository with a committed SourceImportConfiguration joins as in B1, and repositories without one appear neither in the catalog nor in its SourceImportStatus table.

### Outcome 2 — Prompt first visibility

#### B6 — A valid new source appears with exact provenance

**Given** a source joined as in B1, **when** the next normal sync and its automatic refresh complete, **then** the division catalog lists the source repository, the exact commit imported from its default branch, and every concept in its committed ontology without curation. The owner has taken no second action.

#### B7 — First visibility is demonstrated in a division copy

**Given** the template repository, **when** its tests run, **then** it ships with no granted or registered sources, links or catalog entries. **Given** a division created from the template with `init-division` and configured with its own App, **when** B1 and B6 are carried out, **then** they pass there, without changes to the template's code.

### Outcome 3 — Unattended updates

#### B8 — A new valid version is imported without a renewed grant

**Given** a joined source, **when** its owner commits a new valid ontology version to the default branch, **then** the next normal sync publishes that commit and its concepts in the catalog, with no renewed grant, registration or approval.

#### B9 — Repository-owned configuration changes are followed

**Given** a joined source, **when** its owner commits a change to its SourceImportConfiguration, such as a moved ontology path, **then** the next normal sync uses the new configuration. If the changed configuration is invalid, B11 applies.

### Outcome 4 — Actionable import status

#### B10 — Every joined source has a status in the catalog

**Given** a division with joined sources, **when** the catalog is generated, **then** it contains a SourceImportStatus for each granted source showing its repository, status, last attempted commit, last published commit and, for a failure, a reason the App owner can act on. Readers of the catalog see the same status and can judge freshness from it. Reasons contain no credentials or token values.

#### B11 — An invalid new version keeps the last valid one

**Given** a source with a published version, **when** a later commit has missing or invalid configuration, an unreadable ontology path or invalid Turtle, **then** the last valid version and its commit remain in the catalog, the source is marked stale, and its status is `invalid` with the failing commit and reason. When a later commit is valid, it is published and the status returns to `current`.

#### B12 — An invalid first version is reported but not published

**Given** a newly granted source that has never published, **when** its default branch is invalid as in B11, **then** none of its concepts appear in the catalog and its status is `invalid` with the attempted commit and reason.

#### B13 — A selected repository without configuration is reported

**Given** a repository added to the App's selected repositories without a committed SourceImportConfiguration, **when** sync runs, **then** none of its concepts appear and its status is `not configured`.

#### B14 — Access removal leaves the last version marked stale

**Given** a published source, **when** its App access is removed, **then** the last published version stays in the catalog, marked stale, and its status is `access removed` with the last published commit. **When** access is granted again, **then** the next normal sync resumes as in B8. A never-published source whose access is removed leaves the status table.

#### B15 — One failing source does not hold back others

**Given** several sources where one is invalid, unconfigured or inaccessible, **when** sync runs, **then** every valid source is published or updated in the same refresh, and the failing source is reported as in B11–B14.

#### B16 — A conflicting namespace is rejected

**Given** a granted source whose configured namespace is the division core namespace or one already used by another source, **when** sync runs, **then** the new claimant is `invalid` with a namespace-conflict reason, and the source that already holds the namespace, or the core, is unaffected.

#### B17 — Unchanged status produces no refresh

**Given** no source commit, configuration, access or status has changed since the last published catalog, **when** sync runs, **then** no catalog refresh is proposed. The status records commits and states, not attempt times.

## Uncertainties

| ID | Question | Decision or explicit deferral | Basis |
| --- | --- | --- | --- |
| U1 | What makes a granted repository a registered source without a steward-gated `sources.json` change? | The App installation's repositories with a SourceImportConfiguration are the granted sources. `sources.json` remains for steward-registered sources. | User decision. B1, B3. |
| U2 | Does a refresh that adds or updates a granted source need steward review? | No. It is automatic when no reviewed link, linked concept or core term changes. | User decision; vision outcome 1. B2. |
| U3 | What does the catalog show after access is removed? | The last published version stays, marked stale with status `access removed`. | User decision. B14. |
| U4 | Where does the App owner see import status? | In the generated catalog, as a status table also visible to readers. | User decision. B10. |
| U5 | How is an all-repositories installation treated? | Only repositories with a committed SourceImportConfiguration join. Others are ignored, not listed as `not configured`. | User decision. B5, B13. |
| U6 | Which revision is imported? | The head of the source's default branch at sync time. | Analyst decision, for review; matches routine sync in vision outcome 3. B6, B8. |
| U7 | What if a repository is both granted and steward-registered? | One source; the steward registration governs. | Analyst decision, for review; keeps reviewed registrations authoritative. B3. |
| U8 | What if two sources, or a source and the core, claim one namespace? | The new claimant is invalid; the existing holder is unaffected. | Analyst decision, for review; GOVERNANCE.md keeps source identities distinct. B16. |
| U9 | How are behaviours accepted, given the template must stay empty? | In a division created with `init-division` and its own App; automated tests use a fixture division. | Vision outcome 2 as amended in PR #77. B7. |
| U10 | Which file and format make up the SourceImportConfiguration? | Deferred to architecture, coordinating with InternalOntology's committed configuration. It must be committed in the source repository and give at least the ontology path and namespace. | Vision research; architect's scope. |
| U11 | How is a new grant or removal discovered: installation events, polling or both? | Deferred to architecture. It must make B1 visible after the next normal sync. | Vision research names the installation-repository event as an option. |
| U12 | Do organisation policies allow repository owners to add each division's App? | Deferred to acceptance planning as an assumption to confirm in the target organisation before B1 passes. | Amended vision assumption; user confirmed owners can add preapproved Apps. |
| U13 | What happens to a source whose repository is renamed, transferred or archived? | Explicitly deferred, out of scope for this release. | Not covered by the vision's outcomes. |
| U14 | Can a granted source propose links or change shared terms? | No. Links and core terms keep their reviewed governance. | Vision out of scope. B2. |

## Terms

The following shared terms describe how sources join a division catalog. They are proposed in `core/ontology.ttl` for division-steward review. Their definitions come from the accepted vision and the decisions in this document. No existing term changes meaning, no source-owned ontology is rewritten and no equivalence links are proposed.

| Canonical label | Definition and intended scope | Evidence |
| --- | --- | --- |
| SourceAccessGrant | A source repository administrator's grant of read-only access to one division's App, which is that owner's consent to import the repository's whole committed ontology into that division's catalog and registers the repository as a source without steward approval. | Vision outcome 1; U1; B1–B5. |
| SourceImportConfiguration | Repository-owned committed configuration in a source repository that identifies its ontology path and namespace for import. | Vision research; U10; B1, B9, B13. |
| SourceImportStatus | The catalog's per-source record of a granted source's state (`current`, `invalid`, `not configured` or `access removed`), last attempted commit, last published commit and any actionable failure reason. | Vision outcome 4; U3–U4; B10–B17. |

Existing vocabulary:

- **Steward-registered source:** a source listed in `sources.json`, approved by a division steward; unchanged by this feature.
- **Stale:** a source whose published version is not its last attempted commit, because of an invalid version or removed access.

The proposed terms add definitions only. Existing IRIs and links are preserved. Generated catalog changes belong in the separate scheduled-sync PR required by AGENTS.md.
