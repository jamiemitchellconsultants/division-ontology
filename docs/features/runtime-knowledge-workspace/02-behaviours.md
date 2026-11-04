# Runtime knowledge workspace — behaviours

**Feature:** [#25](https://github.com/jamiemitchellconsultants/division-ontology/issues/25)

**Phase:** [#27 — Analyst behaviours](https://github.com/jamiemitchellconsultants/division-ontology/issues/27)

**Status:** Accepted by the user on 4 November 2026; pending division-steward review and merge of the analyst phase pull request.

**Basis:** [Accepted product vision](01-vision.md), the product-owner handoff on #25, and the analyst decisions recorded with the user. Each numbered outcome below matches the vision. Scenarios specify observable behaviour; they do not prescribe components or deployment.

## Behaviours

### Outcome 1 — Architects can discover the available landscape

#### B1 — Discover the published inventory

**Given** a deployed published baseline and a reader with workspace access, **when** the reader explores its inventory, **then** every published entity and relationship is discoverable, including entities with no relationships, and each identifies its source, source commit and PublishedKnowledgeSnapshot. Missing corporate coverage is not represented as complete coverage.

#### B2 — Find business concepts through semantic discovery

**Given** an agreed acceptance example in which a business phrase refers to a published entity through its description or an active KnowledgeAnnotation, **when** a reader searches using that phrase, **then** the expected entity is retrievable with its relevant annotation and provenance. Search uses semantic similarity supported by embeddings; a search match creates no equivalence assertion or reviewed link. A search with no matches reports no matches.

#### B3 — Keep identities distinct

**Given** entities from different sources with equal labels but distinct source-owned identifiers, **when** a reader searches, opens or traverses them, **then** they remain distinct and their source-owned identities and provenance are visible. Contributions attach to the identified entity rather than its label.

#### B4 — Publish a coherent baseline

**Given** an existing baseline and retained runtime contributions, **when** a replacement baseline is accepted for use, **then** each query result identifies the snapshot it used and resolves endpoint availability against that snapshot. **Given** an invalid replacement, **when** publication fails, **then** the last valid baseline and saved contributions remain available without a partially published replacement.

#### B5 — Retain contributions when an entity disappears

**Given** a published entity with saved KnowledgeAnnotations or RuntimeRelationships, **when** a replacement snapshot omits that entity, **then** the contributions, stable endpoint identifier and last-known provenance remain available for inspection, with the missing entity clearly marked unavailable. If a later snapshot restores the same source-owned identifier, its retained contributions are again associated with that available entity; equal labels alone do not restore the association.

#### B6 — Traverse unavailable endpoints only on request

**Given** a retained RuntimeRelationship with an unavailable endpoint, **when** a reader runs a default traversal, **then** that relationship is excluded. **When** the reader explicitly includes unavailable endpoints, **then** the relationship can be traversed and the unavailable endpoint and last-known provenance are clearly marked. The result does not imply current publication of that endpoint.

### Outcome 2 — Business context can be contributed and retrieved

#### B7 — Save and retrieve entity context

**Given** an available published entity and a contributor, **when** the contributor saves a non-empty business explanation, way of working or descriptive business RBAC note against that entity, **then** a KnowledgeAnnotation is persisted with its identity, entity identifier, content, category, author and creation time. A reader querying that entity after the save completes can retrieve it, including after closing and reopening the workspace. A failed save reports failure and does not claim the annotation was saved.

#### B8 — Return annotations during traversal

**Given** a traversal reaching entities with active KnowledgeAnnotations, **when** a reader requests the traversal, **then** the relevant annotations are retrievable alongside their entities, including business RBAC descriptions, with their attribution and runtime origin visible.

#### B9 — Share editing and removal authority

**Given** an existing KnowledgeAnnotation or RuntimeRelationship, including one created by another person, **when** a user with ManageKnowledgeGraph edits or removes it, **then** the workspace permits the change and records the acting user. ReadKnowledgeGraph alone grants neither operation.

#### B10 — Preserve history on edit and removal

**Given** a saved runtime contribution, **when** an authorised change succeeds, **then** its identity remains stable and a KnowledgeContributionChange preserves the previous content, new content or removal, acting user and change time. Removal excludes the contribution from normal discovery and traversal results while retaining its content and history for change-log inspection. Editing or removal does not modify the published baseline.

#### B11 — Resolve stale edits explicitly

**Given** two contributors editing the same version and the first has saved, **when** the second attempts to save the stale version, **then** the newer version is preserved, the second user's unsaved input remains available, and the workspace asks that user to review the newer version before saving again. The same protection applies to removal based on a stale version.

#### B12 — Display business RBAC as information

**Given** a KnowledgeAnnotation describing a business restriction, **when** a reader traverses the associated graph, **then** the description is returned as context and does not itself block or permit a path. Workspace access continues to follow the roles in B15–B18.

### Outcome 3 — The knowledge supports clearer data product definitions

#### B13 — Retrieve context for a pilot product definition

**Given** the pilot data product and representative business queries selected during acceptance planning, **when** its owner explores the available entities, shared concepts and annotations, **then** the owner can retrieve their meanings and provenance together to inform a written product definition. This scenario requires no direct Snowflake ingestion or in-workspace document editor.

#### B14 — Validate business understanding

**Given** the owner's definition informed by B13 and the nominated wider-business reviewer, **when** the reviewer reads the definition during acceptance, **then** the reviewer can explain the product's purpose and intended use, and the acceptance record captures the explanation and outcome. This is a human acceptance measure, not an automatic assessment of prose.

### Outcome 4 — Contribution uses externally managed access control

#### B15 — Authenticate using the deployment's external provider

**Given** a deployment configured for its externally managed Microsoft Entra or Keycloak provider, **when** a user authenticates and presents a valid JWT issued for this workspace by the trusted provider, **then** the workspace applies the token's workspace roles. Each provider must pass the same access scenarios in its corresponding deployment; the workspace does not administer IdP users or role assignments.

#### B16 — Enforce reader and contributor capabilities

**Given** a valid JWT carrying ReadKnowledgeGraph, **when** its user reads or queries workspace knowledge or inspects contribution history, **then** access is allowed, and contribution attempts are denied without persisted changes. **Given** ManageKnowledgeGraph, with or without ReadKnowledgeGraph, **when** its user reads, queries, authors, edits, removes or explicitly associates runtime contributions with published codifications, **then** the corresponding operation is allowed subject to its validation rules. Both roles together have the same capabilities as ManageKnowledgeGraph.

#### B17 — Deny missing roles and invalid authentication

**Given** no JWT, an invalid or expired JWT, an untrusted issuer, an incorrect audience, or a valid JWT containing neither workspace role, **when** workspace data or a contribution operation is requested, **then** access is denied and no contribution is persisted. A valid token without a workspace role receives no implicit ReadKnowledgeGraph access.

#### B18 — Re-evaluate access for subsequent operations

**Given** a user who previously held a workspace role, **when** a subsequent request supplies an expired token or a valid token that no longer grants that role, **then** the request is denied unless its current token still grants the required capability. User input may be retained locally for reauthentication, but the workspace does not save it through an unauthorised request. Immediate revocation of an otherwise valid issued token follows the external provider's contract.

### Outcome 5 — Human-known relationships are immediately usable, with optional codification

#### B19 — Save relationships across sources

**Given** two available entities identified in the published baseline, **when** a contributor saves a RuntimeRelationship between them, **then** it retains both source-owned endpoint identities, the selected direction, stated meaning, author, creation time and each endpoint's source and snapshot provenance. The supported cases include business entities in different systems and architectural components in separate repositories once those components are published.

#### B20 — State relationship meaning

**Given** a contributor defining a RuntimeRelationship, **when** they specify its meaning, **then** they can select an applicable published relationship type or supply a non-empty custom label and explanation. The saved relationship identifies which choice was made. A custom meaning remains a runtime contribution and does not create a shared ontology term or reviewed equivalence link automatically.

#### B21 — Reject incomplete relationship creation

**Given** a new relationship without resolvable available endpoint identifiers or without the meaning required by B20, **when** a contributor attempts to save it, **then** the workspace identifies the validation failure and creates neither a partial relationship nor a successful-change log entry. An entity that becomes unavailable before the save completes is handled as a validation failure, rather than being silently rebound by label.

#### B22 — Make a successful save immediately usable

**Given** a valid RuntimeRelationship, **when** its save succeeds, **then** a reader's subsequent query can discover and traverse it alongside published relationships, with its runtime origin visible. A success response means the relationship and its KnowledgeContributionChange have both been persisted; a failure does not report success or leave a relationship without its required log record. It remains usable without repository codification.

#### B23 — Inspect contributions for optional codification

**Given** runtime contribution history and a division ontology owner holding a workspace reader or contributor role, **when** the owner inspects the change log, **then** they can retrieve each successful creation, edit and removal with its acting user, time, stable contribution identifier, before/after content, relationship endpoints, stated meaning and recorded source provenance. Their repository responsibility grants no extra application role. Repository codification follows the existing reviewed process outside the runtime save operation.

#### B24 — Associate a reviewed publication explicitly

**Given** a RuntimeRelationship and a published relationship, **when** a contributor explicitly records that the latter codifies the former, **then** a CodificationAssociation records the two identities and the acting user and time, and readers can inspect both provenances together. Neither shared labels, equal endpoints nor semantic similarity creates this association automatically. The association does not erase either record or change its authority.

#### B25 — Preserve identifiable records during combined traversal

**Given** a linked runtime and published relationship, **when** readers discover or traverse them, **then** the association and both provenances are available, and each underlying record remains identifiable. Association alone does not merge contributions, rewrite a published assertion, or assert that endpoints are equivalent. If either record later becomes unavailable or the runtime record is removed, the retained history remains inspectable and normal visibility follows B5, B6 and B10.

## Uncertainties

| ID | Question | Decision or explicit deferral | Basis |
| --- | --- | --- | --- |
| U1 | What happens when a published entity disappears? | Retain contributions and mark the entity unavailable; reattach only to the same stable identifier if restored. | User confirmed retention and accepted the restoration rule in draft review. |
| U2 | Who can edit and remove another person's contribution? | Any user with ManageKnowledgeGraph. | Confirmed by user. |
| U3 | Do unavailable endpoints participate in traversal? | Exclude by default; permit explicit inclusion with clear markings. | Confirmed by user. |
| U4 | What survives editing or removal? | Preserve prior versions, acting user and timestamps; removal hides contributions from normal results. | Confirmed by user. |
| U5 | How is optional codification recognised? | A contributor explicitly associates the runtime and published records; show both provenances together. | Confirmed by user. |
| U6 | What happens to simultaneous edits? | Reject a stale save and require review of the newer version before resaving; preserve unsaved input. Apply the same protection to stale removal. | User confirmed review before resaving and accepted the preservation and removal details in draft review. |
| U7 | Must runtime meanings already exist in the published graph? | Use a published relationship type or a custom label and explanation. | Confirmed by user. |
| U8 | Does a valid token without workspace roles get default reader access? | Deny until the external IdP supplies a workspace role. ManageKnowledgeGraph includes read capability. | User confirmed denial; capability follows accepted vision. |
| U9 | What does 'immediately visible' mean? | A subsequent reader query after a successful save can retrieve and traverse the relationship, with its log persisted. No manual repository sync or codification is needed. | Analyst precision accepted in draft review, based on vision outcome 5. |
| U10 | How are published and runtime records distinguished? | Preserve stable identities and source/snapshot provenance; mark runtime origin. Replacing the baseline preserves contributions and does not partially publish. | Accepted vision and repository governance; failure behaviour accepted in draft review. |
| U11 | What graph identifiers and contracts will InternalOntology and C4 supply? | Concrete external formats, entity identifiers, relationship types and example artefacts are explicitly deferred to architecture coordination, outside this phase. Confirm them before implementation planning; never substitute labels for identifiers. C4 diagram generation remains outside feature 25. | Product-owner handoff; no accepted local contracts found. |
| U12 | Which pilot and reviewer demonstrate business clarity? | Nomination of the pilot product, its owner, wider-business reviewer, representative queries and expected matches is explicitly deferred to acceptance planning, outside this phase. They are required before B2, B13 and B14 can pass acceptance. | Accepted vision. |
| U13 | Which query language, embedding model, database and provider settings apply? | Technology, interfaces, ingestion/replacement mechanism, token-role mapping and deployment details are explicitly deferred to architecture. They must preserve these behaviours. | Analyst/architect phase boundary and vision delivery constraints. |
| U14 | Are business RBAC descriptions enforced as graph rules? | No; descriptive playback only in this release. Enforce workspace JWT roles separately. | Accepted vision. |
| U15 | Can contributors change source definitions or automatically codify relationships? | No runtime baseline editing or automatic codification. Reviewed repository changes remain optional and external to the runtime save. | Accepted vision and GOVERNANCE.md. |
| U16 | Is the change log restricted to a separate owner role? | Readers and contributors may inspect it; the division ontology owner uses one of these roles. No extra application role is introduced. | Accepted in draft review, based on the vision's two-role model. |

## Terms

The following shared terms describe knowledge across participating repositories and are proposed in `core/ontology.ttl` for division-steward review. Their definitions are sourced from the accepted vision and the decisions in this document. Existing StaffId and reviewed source links retain their meanings. No source-owned ontology is rewritten and no new equivalence links are proposed.

| Canonical label | Definition and intended scope | Evidence |
| --- | --- | --- |
| PublishedKnowledgeSnapshot | An identifiable published baseline of source-owned knowledge, recording the participating source revisions against which entities and relationships are resolved. | Vision outcomes 1 and 5; B1, B4–B6. |
| KnowledgeAnnotation | Persistent human-authored context attached to an identified published entity, such as a business explanation, way of working or descriptive business RBAC information; it retains runtime provenance. | Vision outcome 2; B7–B12. |
| RuntimeRelationship | A persistent human-authored relationship between identified published entities, with stated meaning and runtime provenance, usable independently of repository codification. | Vision outcome 5; B19–B22. |
| KnowledgeContributionChange | A retained record of a successful creation, edit or removal of a runtime annotation or relationship, preserving its identity, acting user, time and affected content and provenance. | Vision outcome 5 and user decision U4; B10, B22–B23. |
| CodificationAssociation | An explicitly recorded association identifying a published relationship as a codification of a runtime relationship while preserving both records and their provenance. It is not an equivalence assertion between endpoint entities. | User decision U5; B24–B25. |

Existing vocabulary and application identifiers:

- **Entity identifier:** the source-owned stable identifier used by a published graph; matching labels do not establish identity. The external contracts must confirm their identifiers before planning.
- **Unavailable endpoint:** an identified entity absent from the snapshot currently being queried; its retained identifier and provenance remain inspectable.
- **ReadKnowledgeGraph / ManageKnowledgeGraph:** externally assigned workspace role claims with the capabilities in B16; role assignment remains the IdP's responsibility.
- **Business RBAC information:** descriptive business context, separate from the workspace's enforced access roles.

The proposed terms add definitions only. Existing IRIs and links are preserved. Generated catalog changes are validated locally and belong in the separate scheduled-sync PR required by AGENTS.md.
