# Division ontology catalog

Shared core version: 0.4.0. Concepts: 11.

## Sources

| Repository | Commit | Concepts |
| --- | --- | ---: |

## Shared vocabulary

| Term | IRI | Definition |
| --- | --- | --- |
| `C4Diagram` | https://github.com/jamiemitchellconsultants/division-ontology#C4Diagram | A source-backed architectural view of a selected focus at system-context, container, component or code level, tied to a PublishedKnowledgeSnapshot and showing the evidence and gaps relevant to that view. |
| `CodificationAssociation` | https://github.com/jamiemitchellconsultants/division-ontology#CodificationAssociation | An explicitly recorded association identifying a published relationship as a codification of a runtime relationship while preserving both records and their provenance. It is not an equivalence assertion between endpoint entities. |
| `ImpactAssessment` | https://github.com/jamiemitchellconsultants/division-ontology#ImpactAssessment | An architect-authored, reviewable account of the likely effects of a change request across participating sources, identifying assessed elements, reasons, evidence, source revisions and unresolved gaps. It does not approve the request. |
| `KnowledgeAnnotation` | https://github.com/jamiemitchellconsultants/division-ontology#KnowledgeAnnotation | Persistent human-authored context attached to an identified published entity, such as a business explanation, way of working or descriptive business RBAC information; it retains runtime provenance. |
| `KnowledgeContributionChange` | https://github.com/jamiemitchellconsultants/division-ontology#KnowledgeContributionChange | A retained record of a successful creation, edit or removal of a runtime annotation or relationship, preserving its identity, acting user, time and affected content and provenance. |
| `PublishedKnowledgeSnapshot` | https://github.com/jamiemitchellconsultants/division-ontology#PublishedKnowledgeSnapshot | An identifiable published baseline of source-owned knowledge, recording the participating source revisions against which entities and relationships are resolved. |
| `RuntimeRelationship` | https://github.com/jamiemitchellconsultants/division-ontology#RuntimeRelationship | A persistent human-authored relationship between identified published entities, with stated meaning and runtime provenance, usable independently of repository codification. |
| `SourceAccessGrant` | https://github.com/jamiemitchellconsultants/division-ontology#SourceAccessGrant | A source repository administrator's grant of read-only access to one division's App, which is that owner's consent to import the repository's whole committed ontology into that division's catalog and registers the repository as a source without steward approval. |
| `SourceImportConfiguration` | https://github.com/jamiemitchellconsultants/division-ontology#SourceImportConfiguration | Repository-owned committed configuration in a source repository that identifies its ontology path and namespace for import. |
| `SourceImportStatus` | https://github.com/jamiemitchellconsultants/division-ontology#SourceImportStatus | The catalog's per-source record of a granted source's state (current, invalid, not configured or access removed), last attempted commit, last published commit and any actionable failure reason. |
| `StaffId` | https://github.com/jamiemitchellconsultants/division-ontology#StaffId | The enterprise-wide staff number issued by the HR system and used as the internal primary key by older enterprise systems. Exactly 7 characters: U or N followed by 6 digits, case-insensitive and normalised to uppercase. Unique per member of staff, never reissued and never changed. The identity provider is its source of truth; applications read it from a token claim and do not edit it. |
