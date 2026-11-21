# Knowledge Graph Explorer — product vision

[Feature #145](https://github.com/jamiemitchellconsultants/division-ontology/issues/145) · [Product Owner Vision #146](https://github.com/jamiemitchellconsultants/division-ontology/issues/146)

**Accepted by the product owner on 21 November 2026.** The phase completes when its pull request is reviewed and merged.

## Problem

When an architect asks where an order goes after checkout, they need to follow the handoff across repositories, tell similarly named concepts apart, and see the evidence for each connection. The workspace can already return traversal paths as a list. As people follow several relationships and open evidence along the way, they have to keep track of their starting point and the connections they have seen. They need a graph they can explore one concept at a time, with a way to inspect why two things are connected and return to where they were.

## Users

- **Workspace readers**, primarily architects and analysts tracing a business or system relationship across repositories.
- **Workspace managers** who record business context or a known relationship between published concepts or graph nodes while investigating them.

## Outcomes and measures

1. **Follow a cross-repository handoff.** In the Order Fulfilment walkthrough, a reader starts from Ecommerce’s **Order processing**, follows the handoff to Warehouse Fulfilment, and returns to their previous view. They can repeat the navigation using the keyboard and distinguish the Ecommerce and Warehouse **Order** records by repository.
2. **Explain why two things are connected.** Given a published relationship and a workspace-authored relationship between the same endpoints, a reader can inspect each independently, identify its direction and meaning, and find the available source evidence or the contributor’s explanation without losing their place.
3. **Keep exploring a busy node.** A reader can reach a known relationship beyond the first page while keeping the same concept in focus. In separate walkthroughs, they can tell an isolated node from a view with incomplete source coverage or additional results still to load.
4. **Record context where it was discovered.** A manager saves a note and a relationship from the graph. A separate reader can find both, identify their workspace origin, and see the updated relationship after the manager edits it. The note and relationship are stored in the runtime workspace, and the walkthrough confirms that the Turtle inputs in the adopting and source repositories are unchanged.

## Scope

- Add a graph view to the existing workspace, reachable from search results or an entity page. Centre it on one published concept or graph node and show its direct neighbours, with labelled arrows and repository context. Users can magnify the view, follow a neighbour, and return through their navigation history.
- Show published and workspace-authored relationships together. Make their origins visible and let users inspect node and relationship details. Offer a keyboard-accessible neighbour list for the same navigation.
- Let users choose incoming, outgoing, or both directions and load additional relationships using the pagination work tracked separately. [Traversal cursor pagination #119](https://github.com/jamiemitchellconsultants/division-ontology/issues/119)
- Give managers access from the graph to the existing note and relationship contribution actions. Every user edit is stored only in the runtime workspace. Turtle files in the adopting repository and all source repositories are read-only inputs.

## Out of scope

- Creating standalone workspace nodes or editing published concepts, definitions, or relationships.
- Writing back to Turtle files, generating repository changes, or adding a publication workflow.
- A whole-division map or a canvas that keeps the full route drawn as users explore. The proposed first release uses Back to restore earlier views.
- Implementing traversal pagination or changing the workspace access model.

## Research

- The workspace already supports search, entity details, directed traversal, provenance, and contribution history. Its user guide documents separate **Order** records from Ecommerce and Warehouse, giving the graph a concrete identity case to preserve. [Workspace user guide](../runtime-knowledge-workspace/05-user-guide.md)
- The existing Order Fulfilment walkthrough provides evidence-backed handoffs across Ecommerce, Warehouse, and Accounting. It supplies a representative task for testing whether a reader can follow a connection and explain it. [Workspace acceptance walkthrough](../runtime-knowledge-workspace/05-acceptance.md#pilot-questions-and-definition)
- The workspace already distinguishes published and runtime records and reports source coverage gaps. Managers can add notes and relationships using the existing contribution workflow. [Workspace architecture](../runtime-knowledge-workspace/03-architecture.md#published-inputs-and-identities) · [Contribution guide](../runtime-knowledge-workspace/05-user-guide.md#contribute-business-context)
- Keyboard guidance calls for an equivalent way to carry out actions available by pointer. The neighbour list gives the explorer a keyboard route for following a connection and opening its details. [W3C keyboard guidance](https://www.w3.org/WAI/WCAG22/Understanding/keyboard)
