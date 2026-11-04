# Runtime knowledge workspace — product vision

**Feature:** [#25](https://github.com/jamiemitchellconsultants/division-ontology/issues/25)

**Phase:** [#26 — Product owner vision](https://github.com/jamiemitchellconsultants/division-ontology/issues/26)

**Status:** Accepted by the product owner on 4 November 2026; pending review and merge of the product-owner phase pull request.

## Problem

The corporate data mesh is based on Snowflake. Data product owners find it difficult to describe their products in terms the wider business understands. Repository-generated ontologies and knowledge graphs can provide shared vocabulary and technical relationships, but business-specific explanations, ways of working and externally defined role constraints are not all captured at build time. Users and architects also know business and component relationships that span systems maintained in separate repositories. Users need somewhere to add that context and those relationships, retrieve them alongside the published knowledge, and make their additions visible to the division ontology owner for possible codification in the repository.

Architects are the initial audience. They are currently at the “show me everything” stage: they need to explore the available landscape before narrowing it to particular questions or decisions. A view of the deployed ontology and knowledge graph, enriched with persistent human-authored annotations and relationships, should support that discovery and help data product owners develop business-understandable definitions.

## Users

- **Architect:** the initial user, exploring available entities and relationships and their business context. The related C4 feature supplies the architectural discovery context.
- **Knowledge contributor:** anyone whose externally issued JWT carries the `ManageKnowledgeGraph` role, adding annotations and relationships. Contribution is not reserved for architects or a particular business job title.
- **Knowledge reader:** an authenticated user with the default `ReadKnowledgeGraph` role, exploring and querying the published knowledge and runtime contributions.
- **Data product owner:** a business beneficiary who uses the shared concepts and explanations to improve product definitions, and may contribute or read according to their assigned access role.
- **Division ontology owner:** reviews the runtime change log and decides which contributed relationships merit codification through the repository's existing governance. This repository responsibility does not introduce an additional application role in this vision.

## Outcomes and measures

These are the accepted first-release success measures.

1. **Architects can discover the available landscape.** Measure: every entity and relationship in the deployed baseline is discoverable through the workspace, with its source and published snapshot identifiable. Representative searches using business terms can retrieve the expected entities and their annotations. “Everything” means the available published graph, not an assertion that every corporate asset has already been captured.
2. **Business context can be contributed and retrieved.** Measure: an authorised contributor can save business notes, ways of working and descriptive RBAC information against an entity; readers can retrieve the relevant annotations alongside entities encountered in graph traversal queries. Saved annotations remain available after closing and reopening the workspace.
3. **The knowledge supports clearer data product definitions.** Measure: for an agreed pilot data product, an owner can use the shared concepts and annotations to write a definition that a wider-business reviewer can explain in terms of its purpose and intended use. The pilot and reviewer are to be nominated during acceptance planning.
4. **Contribution is available under externally managed access control.** Measure: a user with `ManageKnowledgeGraph` can read, query and author annotations and relationships; a user with `ReadKnowledgeGraph` can read and query. The application enforces those access roles using valid JWT claims, and can authenticate through the externally managed IdP selected for the deployment.
5. **Human-known relationships are immediately usable, with optional codification.** Measure: a contributor can create a business relationship between entities in different systems and an architectural relationship between components defined in separate repositories. On saving, the relationship is immediately visible to readers and available to graph traversal queries. Each contribution is stored in the runtime database and recorded in a log with its author, time, endpoints, stated meaning and source provenance. The division ontology owner can use that record to decide whether to codify the relationship through a reviewed repository change. Codification is optional: the relationship remains usable in the runtime graph without ever being encoded in the repository.

## Scope

- A combined runtime view of the deployed, fixed repository artefacts and a persistent, shared, user-editable layer of annotations and relationships.
- Exploration of the available ontology and knowledge graph, including architectural entities supplied through the related C4 work as those artefacts become available.
- Business-specific annotations attached to existing entities, including explanations, ways of working and information about roles or restrictions defined outside the graph.
- User-created business relationships between existing entities in different systems, and architectural relationships between components whose definitions come from separate repositories. These relationships live in the runtime database and retain the identity and provenance of their endpoints.
- A change log available to the division ontology owner, recording who contributed a relationship, when, what it connects and what the relationship means, with references to the relevant published sources. It supports optional codification in the repository through the normal review process; codification is not a required stage of a runtime contribution.
- Saved runtime relationships are immediately visible to readers and participate in graph traversal alongside published relationships. They remain usable independently of any decision to codify them, and their runtime origin remains visible.
- Graph traversal queries that return the relevant annotations with the published knowledge they describe. Business RBAC annotations are information to be played back in query results in the first release.
- Semantic discovery supported by embeddings, helping users connect business terminology with entities and their annotations. Similarity remains a discovery aid rather than an automatic assertion of equivalent meaning.
- The `ManageKnowledgeGraph` contributor role and default `ReadKnowledgeGraph` reader role. How a role is assigned remains the external IdP's responsibility.
- OAuth-based access with JWT role claims, supporting Microsoft Entra and Keycloak as pluggable authentication providers selected according to the live or test deployment. The IdP remains externally managed and is not registered as an ontology or knowledge-graph source here.
- Preservation of the distinction between published source knowledge and runtime annotations and relationships, including their provenance. Creating a runtime relationship does not itself make it a reviewed division ontology assertion.

The user has supplied these delivery constraints for the later architecture phase: a standalone Blazor WebAssembly GUI, a backend that applies user inputs, a knowledge graph database and semantic embeddings. Database products, interfaces and deployment structure remain architecture work.

The first-release inventory is the deployed repository artefacts described in the original request. The Snowflake data mesh provides the business use case; adding a new direct Snowflake ingestion capability is outside this scope and needs explicit review if required.

## Out of scope

- Formalising business RBAC annotations into machine-evaluated rules that steer traversal around paths blocked by those rules. This is a possible later extension, distinct from enforcing the workspace's own read and manage roles.
- Operating an IdP or administering users, role definitions or role assignments in the external IdP.
- Editing the deployed baseline or changing source-owned ontology definitions and reviewed equivalence links through runtime contributions. Those remain subject to their existing repository governance.
- Automatic or mandatory codification of runtime relationships into repositories. The first release supplies the relationship and its change log for the division ontology owner's optional decision; any repository changes follow the existing review process.
- Implementing C4 diagram generation, which belongs to [feature #7](https://github.com/jamiemitchellconsultants/division-ontology/issues/7).
- A direct Snowflake connector or automatic extraction of data products from Snowflake, under the initial inventory boundary above.

## Research

- [Current repository outputs](../../../README.md) are generated at build time: `catalog.json` supplies a structured catalogue and `combined.nq` preserves source triples in named graphs, with source provenance. They provide an existing published baseline for the runtime view.
- [Current governance](../../../GOVERNANCE.md) keeps source repositories authoritative for their definitions, preserves stable IRIs and requires review for shared terms and cross-source links. Runtime annotations and relationships need to remain distinguishable from those reviewed assertions; the owner's possible codification follows the same governance, and semantic similarity must not silently establish equivalence.
- [C4 diagrams from source knowledge graphs, feature #7](https://github.com/jamiemitchellconsultants/division-ontology/issues/7) aims to support system context, container, component and code views using versioned source knowledge and provenance. It permits incomplete coverage to remain visible. Its vision phase is still open, so this feature does not assume an accepted C4 graph contract.
- The product-owner discussion on 3 November 2026 established the Snowflake data-mesh problem, architects as the initial audience, open contribution subject to the two JWT roles, externally managed pluggable IdPs, and descriptive business RBAC annotations in the first release.
- The same discussion added two relationship use cases: business relationships between elements of different systems, and architectural relationships between components defined in separate repositories. Both require runtime persistence and a log for the division ontology owner to consider for repository codification. The user confirmed immediate visibility and traversal on saving, with codification optional.
- The original request identifies knowledge-graph work in InternalOntology as forthcoming. Its published graph contract and stable entity identifiers need confirmation with that work and the C4 feature before implementation planning.
