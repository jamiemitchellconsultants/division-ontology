---
feature: 198
slug: generalise-impact-assessment
phase: po
---

# Generalise impact assessment beyond C4

**Feature:** [#198](https://github.com/jamiemitchellconsultants/division-ontology/issues/198), repurposed from [#197](https://github.com/jamiemitchellconsultants/division-ontology/issues/197)

**Status:** Accepted by the product owner on 26 November 2026.

## Problem

The C4 pilot lets an architect turn a change request into an impact assessment that cites evidence. It searches source graphs for leads, follows directed relationships to likely impacts, and records what was confirmed, rejected and left unresolved. But it works only for graph nodes that a steward has mapped to C4 levels. It runs only as a local command with a clean checkout of every source, and it treats code as the evidence that matters. The Confluence ontology import will give divisions group repositories whose graphs preserve policy and process knowledge, with each claim citing lines of an exported page. Architects and business analysts need to ask what a change to a policy, a process or a documented system affects, in this group and in others, and to see the page evidence behind each step. Today they cannot. The explorer lets them find and trace knowledge, but its traversal follows every relationship with no notion of impact, and it produces no record anyone can review or share.

## Users

- **Architect:** already uses the C4 pilot for system changes. Needs the same assessment over knowledge graphs, including systems whose only architecture evidence is documentation, and needs C4 views to keep working where mappings exist.
- **Business or process analyst:** assesses changes to policies, processes and rules. Signs in to the division's workspace and works in the browser. Does not use a command line or hold source checkouts.
- **Workspace reader:** anyone with read access to the workspace. Opens a saved assessment to see what it found, on which evidence, and what it left unresolved.

Source owner teams and division stewards are affected without being users of this feature. Owner teams maintain the evidence that assessments cite. Stewards review the links that assessments may cross, and the relationship master list that gives relationships their impact meaning.

## Outcomes and measures

1. **Any evidenced graph can be assessed, with or without C4 mappings.** Measure: on a fixture source shaped like a Confluence group graph, with no C4 mappings, candidate search returns leads that are supported because they have evidence, and impact paths start from them. On the existing C4 fixtures, search, paths, views and assessments give the same results as today.
2. **Impact follows the meaning of each relationship.** Measure: on a fixture whose relationships are in the division's relationship master list, a path takes a step only where the list says that relationship carries impact, and only in the direction it gives. A relationship missing from the list is not followed, and the assessment reports it as an unresolved gap. A missing relationship never counts as proof of no impact.
3. **Impact crosses sources only through reviewed links.** Measure: on a fixture with two sources whose terms share a reviewed link to a shared term, a path crosses from one source to the other through that link and cites the link. On the same fixture with matching labels but no reviewed link, no path crosses, and the assessment reports the boundary as a gap.
4. **Analysts can assess in the workspace, and readers can open the result.** Measure: a signed-in analyst goes from a change request description to a saved assessment in the browser, with no command line and no source checkout. Another reader opens the saved assessment and sees the same leads, paths, evidence and gaps. Its Markdown export matches what the command line renders for the same snapshot and inputs.
5. **Document evidence can be read where it is cited.** Measure: for every cited line range in an exported page, the assessment shows the page's title, its original Confluence address and the cited lines, all from the pinned commit. Code evidence is shown as it is today.
6. **Every assessment stays pinned and honest about its limits.** Measure: a saved assessment records the snapshot it used and is refused against any other. It carries the snapshot's coverage gaps and its own unresolved gaps, and it has no approval or sign-off state.

## Scope

- One impact assessment engine for any source graph that passes validation, with C4 views as an optional layer where mappings exist.
- Impact meaning for each relationship, read from the division's relationship master list.
- Paths that cross sources through steward-reviewed links (`exact` and `specializes`), as well as through the existing C4 bindings.
- Document evidence in leads, paths and assessments: inspection, verification at the pinned commit, and display.
- Running, saving, sharing and exporting assessments in the runtime knowledge workspace, through its API and web UI.
- Keeping the command line, so architects can still assess from source checkouts.

## Out of scope

- Creating the relationship master list. It is a prerequisite feature, parked as [#205](https://github.com/jamiemitchellconsultants/division-ontology/issues/205), with its own vision, which defines its words, their definitions, their impact direction and how stewards review additions.
- Importing source graphs into the published catalog ([#197](https://github.com/jamiemitchellconsultants/division-ontology/issues/197)).
- Approval, sign-off or workflow states on assessments.
- Model-assisted candidate search, model-proposed impacts, or probability or decay scores on paths.
- Inferring links or equivalences between sources from matching labels or claims.
- Editing graph claims, terms or links from an assessment.
- New C4 levels or roles, and changes to the ontology kit, its graph schema or source repositories.

## Research

- **Most of the C4 engine is not about C4.** The pinned snapshot, graph loader, candidate search, directed impact paths and assessment record do not depend on architecture. They read kit source graphs and verify evidence by blob hash, line range, role selector and coverage, which works the same for code and for Markdown pages. C4 enters in two places: impact paths refuse any start without a C4 mapping (`unsupported-start`), and candidate search counts a lead as supported only if it has a mapping. Mappings accept evidence of any role, so a graph built from architecture documents could already back C4 views. Sources: `src/c4/paths.mjs`, `src/c4/candidates.mjs`, `src/c4/registry.mjs`, `src/c4/graph.mjs`, `src/c4/project.mjs` at `23c247d`.
  - *Implication:* outcome 1 removes a constraint rather than adding an engine. The architect should keep C4 behaviour identical on the existing fixtures.
- **The workspace already traverses graph claims, but with no notion of impact.** `POST /api/traverse` walks published and contributed edges outgoing, incoming or both, up to depth 8, with cursor pagination. The published projection already includes graph nodes, their assertions, `denotes` edges from nodes to terms, and `reviewedLink` edges from local to shared terms. Walking both ways can therefore already cross sources, but it follows every edge in both directions. Sources: `runtime/RuntimeKnowledge.Contracts/Published.cs`, `runtime/RuntimeKnowledge.Store/GraphQueries.cs`, `src/workspace/bundle.mjs`.
  - *Implication:* the workspace has the graph that outcomes 2 to 4 need. What it lacks is impact direction, lead search tied to evidence, and a saved record.
- **The workspace does not hold evidence text.** Published provenance carries each evidence record's path, line range, blob id and role, but not the cited text. The workspace bundle packages the cited files' bytes, but the runtime reads only the projection. Source: `runtime/RuntimeKnowledge.Store/BundleReader.cs`, `src/workspace/bundle.mjs`.
  - *Implication:* outcome 5 needs the runtime to serve cited text from the pinned bundle. That keeps page text inside the access-controlled workspace, rather than in a public catalog. The architect should decide how.
- **Writing in the workspace needs the manage role.** Readers have `ReadKnowledgeGraph`. Every contribution command needs `ManageKnowledgeGraph`. The existing contribution kinds are annotations, relationships and codifications. Source: `runtime/RuntimeKnowledge.Api/Identity.cs`, `runtime/RuntimeKnowledge.Api/WorkspaceRoutes.cs`.
  - *Implication:* the analyst must decide who may save an assessment: any reader, or only users with the manage role.
- **Reviewed links are `exact` or `specializes`.** `exact` requires equal labels and is written as `owl:equivalentClass`. `specializes` is written as `rdfs:subClassOf`. The template ships with no links. Source: `src/catalog.mjs`.
  - *Implication:* impact may cross the two kinds differently. A change to a general term may affect its specialisations more often than the reverse. The analyst should decide the crossing rule for each kind.
- **No relationship master list exists yet.** The division core defines no relationship properties, and graph assertions accept any relationship IRI. Only the C4 projection treats `kit:contains` specially. The Confluence import design expects the division to hold the list, with a definition for each word and additions proposed by fork and pull request, and its graph phase cannot start without it. It is now parked as #205. Sources: `core/ontology.ttl`, `src/c4/graph.mjs`, `src/c4/project.mjs`, the `confluence-ontology-import` design dated 26 November 2026 (private, summarised only).
  - *Implication:* outcome 2 depends on the prerequisite feature. That feature should give each word an impact direction, not just a definition.
- **Typed relationships are what keep impact analysis useful.** Requirements traceability research finds that impact propagation without relationship semantics produces many false positives. Rule-based approaches derive propagation from the meaning of each relationship and the kind of change. Sources: [Goknil, Kurtev and van den Berg, change impact analysis for requirements](https://ris.utwente.nl/ws/files/246928113/Goknil2009change.pdf), [Lehnert et al., rule-based impact analysis](https://www.inf.uni-hamburg.de/en/inst/ab/swk/research/publications/pdf/2013-lehnert_impact-analysis_csmr2013.pdf), [Change impact analysis (Wikipedia)](https://en.wikipedia.org/wiki/Change_impact_analysis).
  - *Implication:* this supports making impact meaning part of the master list (outcome 2) rather than following every relationship.
- **Policy impact tools insist on traceable evidence.** Regulatory and policy impact work commonly links policy clauses, documents and processes in a graph and traces each finding to its source. Most published accounts are vendor case studies or early research, so their claimed time savings are unverified. Sources: [RegNLP 2025 policy monitoring framework](https://aclanthology.org/2025.regnlp-1.4.pdf), [Oliver Wyman and MIT Sloan policy graph project](https://mitsloan.mit.edu/sites/default/files/inline-files/Oliver_Wyman_Zhifei_Li_Nathan_Wangidjaja.pdf), [GraphAware governance case study](https://graphaware.com/pdf/GraphAware-Governance-CaseStudy.pdf).
  - *Implication:* the pilot's rule that every impact cites a path and evidence carries over to analysts unchanged.
