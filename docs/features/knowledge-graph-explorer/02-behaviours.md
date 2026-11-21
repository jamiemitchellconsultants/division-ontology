# Knowledge Graph Explorer — behaviours

[Feature #145](https://github.com/jamiemitchellconsultants/division-ontology/issues/145) · [Analyst Behaviours #147](https://github.com/jamiemitchellconsultants/division-ontology/issues/147)

**Accepted by the user on 21 November 2026.** The phase completes when its pull request is reviewed and merged. The scenarios follow the four outcomes in the [accepted vision](01-vision.md) and the [product-owner handoff](https://github.com/jamiemitchellconsultants/division-ontology/issues/145).

## Behaviours

### Outcome 1 — Follow a cross-repository handoff

#### B1 — Enter the graph at the chosen entity

**Given** an available published concept or graph node, **when** a reader opens its graph from a search result or entity page, **then** the selected entity is in focus and the graph shows its loaded direct neighbours. Each node displays its label and source. If Ecommerce and Warehouse both have a concept labelled **Order**, opening either selects its own source record and shows its own neighbourhood.

#### B2 — Follow relationships with their original direction

**Given** the Order Fulfilment fixture, **when** a reader opens Ecommerce’s **Order processing** and follows direct neighbours through its containing system and the reviewed handoff to Warehouse Fulfilment, **then** each chosen neighbour becomes the focus and shows its own direct connections. The initial view shows both directions. Choosing **Incoming** or **Outgoing** reapplies that filter at the current focus, and arrows always point from the relationship’s source to its destination.

#### B3 — Return to the previous view

**Given** a reader has changed direction, zoomed or panned, and loaded additional relationships, **when** they follow a neighbour and use **Back** within the explorer, **then** the previous focus, direction, viewport, and loaded neighbourhood return. If the explorer has since detected that those results are stale, it restarts at the saved focus and direction and tells the reader that the results were refreshed. If the saved focus is unavailable, the explorer identifies it and offers a route back to search or the last available view.

#### B4 — Navigate and inspect with the keyboard

**Given** a reader is using the keyboard, **when** they open the neighbour list, **then** they can identify each loaded connection’s direction and source, open its details, follow its neighbour, and return with **Back**. Keyboard controls also let the reader change direction, load more, and change or reset magnification. Changing magnification preserves the graph focus and current selection.

#### B5 — Keep the latest focus when requests overlap

**Given** a request is still loading when the reader changes focus or direction, **when** the previous request returns, **then** its result does not replace the newly chosen view. If the new view fails to load, the explorer identifies what failed, offers a retry, and lets the reader return to the last successful view.

### Outcome 2 — Explain why two things are connected

#### B6 — Inspect a node or an edge without losing the view

**Given** a focused node and an adjacent published relationship, **when** a reader inspects either record, **then** the details identify its source, identifier, source revision, and available description or meaning. Relationship details include both endpoints, direction, and any available evidence links. If there is no evidence link, the detail says so. Opening and closing the details preserves focus, loaded neighbours, and viewport.

#### B7 — Inspect parallel relationships independently

**Given** a published relationship and a RuntimeRelationship have the same endpoints, **when** a reader inspects them from the diagram or neighbour list, **then** both can be selected independently, even if their labels match. The published record shows its published provenance. The RuntimeRelationship shows its workspace origin, stated meaning, attribution, and accessible history. Origin is also expressed in text.

### Outcome 3 — Keep exploring a busy node

#### B8 — Load more without moving away

**Given** the graph has a continuation for the selected node and direction, **when** the reader chooses **Load more**, **then** new connections join the current view without duplicate records, while the focus, selection, and viewport stay in place. A known relationship beyond the first page can be reached by continuing. **Given** a page returns zero paths with a TraversalCursor, **then** the graph offers **Load more** again. Once a response carries no continuation, **Load more** disappears.

#### B9 — Explain empty, incomplete, and partial results

**Given** a completed traversal has returned no visible relationships, **when** the graph shows its result, **then** it reports no visible connections for the current direction. If source coverage is incomplete, it shows that gap alongside the result without calling the node isolated. **Given** a traversal can still continue, **when** it has returned no visible relationships so far, **then** the graph offers to check more results. An unavailable endpoint remains excluded under the workspace’s default traversal rules.

#### B10 — Recover from an invalidated or failed continuation

**Given** a published refresh or workspace contribution change invalidates a TraversalCursor, **when** the reader chooses **Load more**, **then** the graph says the results have changed and offers **Reload** for the same focus and direction. Previously loaded results are marked out of date until the reload replaces them from page one. A transient error on an otherwise valid continuation leaves the current view in place and offers a retry.

### Outcome 4 — Record context where it was discovered

#### B11 — Show contribution actions to managers

**Given** a user with `ReadKnowledgeGraph` and a user with `ManageKnowledgeGraph` open the same graph, **when** each opens node and relationship details, **then** both can inspect the available records. Only the manager has contribution controls. The manager can add a KnowledgeAnnotation to an available published entity and create a RuntimeRelationship between available published endpoints. Existing note and RuntimeRelationship details give the manager the applicable edit or remove actions. Write permission is enforced on save, including if the user’s role changes after opening the form.

#### B12 — Create and discover workspace contributions

**Given** a manager chooses to create a RuntimeRelationship from an available focused node, **when** they identify the other published endpoint by source, confirm the direction, supply a valid published type or a custom label and explanation, and save, **then** the editor confirms the save and provides access to the saved record from the explorer. When a separate reader next loads the relevant graph results, they can find the relationship and its workspace origin. A saved KnowledgeAnnotation is likewise visible in the focused node’s details to a subsequent reader. An unsuccessful save shows the reason and keeps the draft available.

#### B13 — Reflect edits and removals in a fresh view

**Given** a manager successfully edits or removes a contribution from the graph, **when** a reader next loads the relevant graph results or details, **then** the reader sees the edited values or the removed record is absent from normal results. Each KnowledgeContributionChange remains available through contribution history. The explorer refreshes the current neighbourhood from page one after its own successful save and preserves the focus and direction if the focus is still available.

#### B14 — Preserve a rejected draft

**Given** a manager has an unsaved draft, **when** a newer contribution version, an unavailable selected endpoint, an expired session, or a revoked permission prevents saving, **then** the graph identifies the reason, preserves the draft for review, and makes no successful change. A stale edit shows the current record for review before resubmission.

#### B15 — Keep repository inputs read-only

**Given** clean, pinned Turtle inputs in the adopting repository and participating source repositories, **when** a manager creates, edits, or removes contributions from the graph, **then** only runtime records and their history change. An acceptance run verifies that the Turtle inputs and repository revisions are identical before and after those actions.

## Uncertainties

| Id | Question | Decision or deferral |
| --- | --- | --- |
| U1 | Which relationships appear first? | Start with both directions and direct available neighbours. Direction changes start fresh at the current node (B2). Defer an unavailable-endpoint toggle, whole-division view, and a persistent route canvas. |
| U2 | What does Back restore? | Restore the prior neighbourhood and its view state. Refresh it when the explorer has detected that it is stale. Persisting navigation across sessions is out of scope (B3). |
| U3 | How do users tell parallel relationships apart? | Let users select each origin and record independently from the diagram or the list. Identical labels and endpoints can coexist (B7). Visual layout and styling are for the architect. |
| U4 | What happens with an empty or stale page? | Follow the continuation even if the latest page was empty. On a stale continuation, have the reader reload the same query from page one. Keep temporary retry separate from reload (B8–B10). [Traversal contract](../traversal-cursor-pagination/02-behaviours.md) |
| U5 | What can a user change from the graph? | Use current manager-only annotation and relationship actions on published endpoints. Standalone runtime nodes, codification actions in the explorer, and repository write-back are out of scope (B11–B15). |
| U6 | What measures success on realistic data? | Use the existing Order Fulfilment fixture for handoffs and source identity. Add scenarios for parallel relationships, empty continuation pages, stale results, and contribution saves (B2, B7–B15). Fixture construction is deferred to implementation and acceptance. [Order Fulfilment walkthrough](../runtime-knowledge-workspace/05-acceptance.md#pilot-questions-and-definition) |

## Terms

The explorer uses existing shared terms from the [division core](../../../core/ontology.ttl). PublishedKnowledgeSnapshot is the baseline for published records. KnowledgeAnnotation and RuntimeRelationship are workspace contributions, and KnowledgeContributionChange records successful changes to them. TraversalPage and TraversalCursor describe the existing traversal continuation contract. **Focus**, **neighbour list**, and **viewport** describe the interface. The feature introduces no new shared ontology terms.
