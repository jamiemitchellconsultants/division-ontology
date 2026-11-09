# Runtime knowledge workspace user guide

## Find published knowledge

1. Open the workspace and sign in through the configured identity provider. A reader can browse, search, traverse and inspect history. A manager can also change contributions. Ask the identity administrator for the required workspace role.
2. In **Published inventory**, open an entity or a relationship. Use **Next page** to continue through the inventory. Check the repository, identifier, source commit and active snapshot. Two records labelled **Order** can belong to different repositories.
3. Enter a business phrase under **Semantic search**. Open the matched entity and check its provenance and annotations. If indexing is pending, try again after the indexer catches up. A completed search can return no matches. Follow graph evidence to establish how the matched records are related.
4. On an entity, select a direction and maximum depth and click **Traverse**. Inspect the steps, their source evidence and the annotations alongside the reached entities. Enable **Include unavailable endpoints** to inspect retained relationships whose endpoints are absent from the active snapshot. The workspace marks these endpoints and shows their last-known provenance. Coverage gaps indicate where source information is missing.

## Contribute business context

As a manager, open an available entity and add a business explanation, way of working or business RBAC annotation. The workspace attributes the note to you. Business RBAC notes are descriptive context that readers see during traversal. Workspace access follows the identity provider’s roles.

To connect two available entities, open **Create a relationship**, choose the destination by source and identifier, and specify the direction and meaning. Select an applicable published relationship type, or provide a custom label and explanation. After saving, a reader can find the relationship in traversal alongside published relationships and inspect its origin and history.

Use the annotation or relationship controls to edit or remove a contribution. Another manager can do this too, and the history records the acting user. If a save or removal reports a version conflict, review the newer version before resubmitting. If your session expires, sign in again and review your draft before retrying.

## Inspect history and codification

Open the annotation or relationship history to see who created, edited or removed it, when the change happened, and what changed. Removed contributions remain visible in history. A baseline change can make an endpoint unavailable. When the same source identifier returns, its retained contributions can be resolved again.

If a published relationship codifies a runtime relationship, a manager can associate them explicitly from the runtime relationship’s detail page. Inspect both records and their provenance from the association. Division ontology owners review the history and use the repository’s review process when they choose to codify a runtime contribution.

## Work through Order Fulfilment

Search for **What happens after a customer places an order?** Open Ecommerce’s **Order processing** lead, then inspect the Ecommerce system’s outgoing traversal. Its evidence describes the handoff to Warehouse Fulfilment for picking and dispatch.

Search for **How does Accounting learn that an order has shipped?** Inspect the Accounting **Order** and Warehouse **Shipment** leads, then follow the Warehouse-to-Accounting handoff. Its evidence says shipment completion is sent to Accounting for sales invoicing. The fixture leaves the delivery mechanism open.

**Example product definition.** Order Fulfilment gives Operations and Finance a shared view of how a customer order moves from Ecommerce to Warehouse dispatch and how shipment completion feeds sales invoicing in Accounting.
