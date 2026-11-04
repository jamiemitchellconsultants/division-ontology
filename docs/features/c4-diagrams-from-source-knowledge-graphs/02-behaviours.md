# C4 diagrams from source knowledge graphs — behaviours

**Feature:** [#7](https://github.com/jamiemitchellconsultants/division-ontology/issues/7)

**Phase:** [#15 — Analyst behaviours](https://github.com/jamiemitchellconsultants/division-ontology/issues/15)

**Status:** Accepted by the product owner on 4 November 2026; pending review and merge of the analyst phase pull request.

**Basis:** [Accepted vision](01-vision.md), the product-owner handoff on #7, the published division ontology and the accepted upstream source graph contract. Each outcome below matches the vision. These scenarios specify observable results without choosing a renderer, storage mechanism or deployment.

## Behaviours

### Outcome 1 — Architects make credible impact assessments

#### B1 — Start from a change request

**Given** a representative change request description whose terms match known source facts and a PublishedKnowledgeSnapshot, **when** an architect begins an impact review without naming a starting system or component, **then** the architect can find the matching candidate elements from participating sources and see why each matched, with its source-owned identifier, repository and source revision. The candidates are leads for the architect to confirm, not an automatic impact conclusion. **Given** a description with no supported match, **when** the architect begins the review, **then** no starting element is invented and the result shows the relevant coverage or an unknown gap. **Given** a known element identifier, **when** the architect supplies it, **then** that identified element can be used as a starting point without relying on a matching label.

#### B2 — Trace possible impact across sources

**Given** one or more architect-confirmed starting elements and a path of source-backed relationships, including an explicit relationship between elements from different participating repositories, **when** the architect examines possible impact, **then** every step remains visible with its direction, endpoints and evidence. A two-step path is not shown as a direct assertion between its endpoints. Equal names in different sources do not create a step or merge the elements.

#### B3 — Produce a reviewable assessment

**Given** an architect has examined a change request, its C4Diagram views and their evidence, **when** the architect produces an ImpactAssessment, **then** it identifies the request, the PublishedKnowledgeSnapshot, the architect's likely affected elements and reasons, the evidence behind each stated impact, and unresolved questions or coverage gaps. The architect can mark a candidate as not expected to be affected with a reason. The assessment is presented for human review and does not approve the request or claim that every possible impact has been found.

#### B4 — Record qualitative pilot feedback

**Given** a representative change request and an architect's ImpactAssessment, **when** the architect and a reviewer evaluate it during the first pilot, **then** the acceptance record captures their feedback on perceived accuracy, completeness, useful discoveries, suspected missed impacts, false leads and confidence, with examples and unresolved questions. The pilot does not require an independently adjudicated reference set or numeric accuracy threshold.

### Outcome 2 — The four C4 levels help investigate impact

#### B5 — Show system context

**Given** source-backed facts about a selected system, its connected people and other systems at one PublishedKnowledgeSnapshot, **when** an architect requests a system-context C4Diagram, **then** the view places that system in focus and shows only supported context relationships. It identifies the system boundary and does not present an unjoined repository as a participating source.

#### B6 — Show containers

**Given** supported facts about applications or data stores within the selected system and their communications, **when** the architect requests a container C4Diagram, **then** the view shows the system boundary, its evidenced containers and their relationships, with directly connected external elements where supported. It does not treat deployment instances as containers solely because they share a name.

#### B7 — Show components

**Given** supported facts about components within a selected container, **when** the architect requests a component C4Diagram, **then** the view shows the container boundary, its evidenced components and their relationships, with relevant directly connected elements where supported. Components from another container are not silently placed inside the selected boundary.

#### B8 — Show important code

**Given** a selected component and supported code-level facts, **when** the architect requests a code C4Diagram, **then** the view shows evidenced code elements and relationships within that component and allows the architect to inspect their source at the recorded commit. When a reliable graph projection is unavailable, code inspected at that commit may support the view, with its source location retained; unsupported code relationships are not invented. The code level can be unavailable while higher levels remain usable.

#### B9 — Follow the supported levels

**Given** a system-context C4Diagram and source facts for deeper levels, **when** the architect follows a selected element toward its internal detail, **then** the next available level retains the selected focus and PublishedKnowledgeSnapshot. If the next level lacks sufficient facts, the architect sees that gap and can continue using the levels that are available.

### Outcome 3 — Assessments are evidence-based and reproducible

#### B10 — Inspect evidence behind a diagram item

**Given** a C4Diagram with source-derived elements and relationships, **when** an architect or reviewer inspects any item, **then** they can find its source-owned identifier, repository and recorded commit. A displayed relationship identifies its supporting graph assertion and source evidence, or the inspected source location used for code-level fallback. A displayed element identifies the published source graph node and the evidence that supports its presence in the view; if that evidence is unavailable, its support is visibly unresolved. Inferred relationships are marked tentative and show the observations behind them.

#### B11 — Keep source identities and revisions distinct

**Given** participating sources with equal labels or different commits, **when** a C4Diagram or ImpactAssessment combines their facts, **then** each element retains its source-owned identifier and source commit within the PublishedKnowledgeSnapshot. Equal labels do not assert equivalence. A claim from an older commit is not presented as current merely because a newer snapshot exists.

#### B12 — Reproduce a pinned view

**Given** a PublishedKnowledgeSnapshot and unchanged source facts, **when** the same C4Diagram is requested again for the same focus and level, **then** it contains the same supported elements, relationships, evidence and coverage indications. **Given** a newer source commit, **when** a new snapshot is used, **then** the view identifies that new revision and does not silently mix its facts with the earlier view.

### Outcome 4 — Missing knowledge does not imply no impact

#### B13 — Show partial and unknown coverage

**Given** a participating source with usable facts at some levels and unknown or excluded areas, **when** an architect requests C4Diagram views or an ImpactAssessment, **then** available facts remain usable and the unsupported levels or areas are visibly marked incomplete or unavailable with their known reasons. An empty relationship result is not presented as proof that no dependency or impact exists.

#### B14 — Keep unjoined repositories out of the participating inventory

**Given** a repository not joined to the division catalogue, **when** an architect explores the participating-source inventory or C4 coverage, **then** that repository has no source entry or coverage claim. A participating source may name an external system as evidenced context, but that does not make the external system's repository a joined source.

#### B15 — Do not gate source admission on C4 coverage

**Given** a valid source registration with facts for only some C4 levels, **when** the source joins or refreshes, **then** its available facts can contribute to supported views and its gaps remain visible. Missing C4 levels do not reject registration or suppress otherwise valid source concepts.

## Uncertainties

| ID | Question | Decision or explicit deferral | Basis |
| --- | --- | --- | --- |
| U1 | Must the architect identify a starting element, or can a change request description be enough? | Description alone is enough to begin. It may return candidates or an explicit no-match and coverage result; a supplied source-owned identifier can start directly. | Product owner confirmed description-only entry on 4 November 2026. |
| U2 | Does the system decide impact automatically? | No. It shows candidates and evidenced paths; the architect selects likely impacts, exclusions and unresolved questions in an ImpactAssessment. | Accepted vision excludes automatic impact decisions. |
| U3 | How is an ImpactAssessment stored or attached to an external change request? | Explicitly deferred to architecture. B3 requires a reviewable output with request identity, findings, evidence, gaps and snapshot, without prescribing persistence or integration. | The vision requires an assessment, but does not choose a platform. |
| U4 | May a cross-repository path be formed by matching labels? | No. Each path step needs an explicit, evidence-backed relationship and source-owned endpoint identities. | Governance and vision provenance requirements. |
| U5 | What if a source graph lacks code-level projection? | Inspect source at the recorded commit when practical; otherwise mark code level unavailable. Neither route invents code relationships. | Accepted vision scope and PO handoff. |
| U6 | Does a missing assertion prove a dependency is absent? | No. Display source coverage and unresolved impact, including unknown or excluded areas. | Vision outcome 4 and upstream GraphCoverage contract. |
| U7 | Which change requests and numeric threshold define pilot success? | The representative requests and feedback participants are deferred to user acceptance planning. The first pilot records qualitative feedback and has no reference set or numeric threshold. | Accepted vision outcome 1 and product-owner steering. |
| U8 | Which visual notation, rendering tool, storage mechanism and graph-to-C4 mapping are used? | Explicitly deferred to architecture. The four view scopes and observable provenance and gap rules above remain required. | Analyst/architect phase boundary. |
| U9 | Can an external system appear in context when its owning repository is not joined? | Yes, if a participating source supplies an evidenced external-system relationship. It is context only, with no participating-source entry or coverage claim for the unjoined repository. | Product owner confirmed referenced external context on 4 November 2026; the unjoined-source boundary remains from the vision. |

## Terms

The following new shared terms are proposed for `core/ontology.ttl` and division-steward review. They are wider than any one source repository. The accepted vision and these behaviours are their evidence; no existing IRI or source-owned ontology is changed, and no equivalence link is proposed.

| Canonical label | Definition and intended scope | Evidence |
| --- | --- | --- |
| C4Diagram | A source-backed architectural view of a selected focus at system-context, container, component or code level, tied to a PublishedKnowledgeSnapshot and showing the evidence and gaps relevant to that view. | Vision outcomes 2–4; B5–B13. |
| ImpactAssessment | An architect-authored, reviewable account of the likely effects of a change request across participating sources, identifying assessed elements, reasons, evidence, source revisions and unresolved gaps. It does not approve the request. | Vision outcome 1; B1–B4 and B13. |

Existing `PublishedKnowledgeSnapshot` retains its meaning. `SourceKnowledgeGraph`, `GraphAssertion`, `GraphEvidence` and `GraphCoverage` are upstream Ontology Kit terms; this feature consumes their published facts and does not redefine them in the division namespace. A **change request** is an externally authored proposal being assessed here, not a new source-owned ontology concept.
