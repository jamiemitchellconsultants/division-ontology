# Use the Knowledge Graph Explorer

The workspace graph shows direct connections around one available published concept or graph node. Readers can explore and inspect records. Managers can add context and maintain the workspace's notes and relationships.

## Start at the entity you mean

Choose **Graph** on a search result or **Open graph** from the entity's details. The graph heading gives the label and owning source. If you see two records called **Order**, check the source in the result or heading to distinguish Ecommerce from Warehouse. [B1](02-behaviours.md#b1--enter-the-graph-at-the-chosen-entity)

The direction starts at **Both**. Choose **Incoming** to see relationships ending at the focus, or **Outgoing** to see relationships starting there. The arrows always point from the source to the destination. To follow a neighbour, double-click its node on the diagram or choose its **Follow** action in the list. [B2](02-behaviours.md#b2--follow-relationships-with-their-original-direction)

## Move around and return

Use **Back** in the explorer to return to the prior focus, direction, loaded connections and zoom or pan. If the explorer knows that the saved results are out of date, it requests a fresh page at the previous focus and tells you it refreshed the view. When that focus is unavailable, use the search link or return to the current view. [B3](02-behaviours.md#b3--return-to-the-previous-view)

With the keyboard, Tab to **Direction** or a neighbour action. Use Enter to choose **Inspect**, **Inspect relationship**, or **Follow**. Opening details from the list moves focus into the panel. **Close details** returns focus to the action that opened it. **Zoom in**, **Zoom out**, and **Reset zoom** are keyboard controls. If the diagram reports a startup failure, use the neighbour list and choose **Retry diagram** when ready. [B4](02-behaviours.md#b4--navigate-and-inspect-with-the-keyboard)

If a new focus or direction fails to load, the last successful view stays available. Choose **Retry** to repeat the request or **Return to current view** to continue from that view. [B5](02-behaviours.md#b5--keep-the-latest-focus-when-requests-overlap)

## Find the meaning and evidence

Choose **Inspect focus**, single-click a node or edge, or use the list's **Inspect** and **Inspect relationship** actions. Details show the record identity, owning source, available description, and provenance. Relationship details show both endpoints in arrow order. A published citation gives the commit and available path or lines. Where configured, **View evidence** opens the commit-pinned source in another tab. Close details to resume exploring with the same connections and diagram view. [B6](02-behaviours.md#b6--inspect-a-node-or-an-edge-without-losing-the-view)

A published relationship and a workspace relationship can link the same two nodes. Inspect each curve or its separate list row. The row and panel identify its origin. A workspace relationship shows its meaning and author and provides **Relationship history**. [B7](02-behaviours.md#b7--inspect-parallel-relationships-independently)

## Explore a busy or incomplete neighbourhood

Choose **Load more** to add the next page to the same view. The explorer keeps the focus, selection, and diagram positions. **Load more** stays when the next page can still be checked, including after an empty page. It disappears once the last page has been loaded. [B8](02-behaviours.md#b8--load-more-without-moving-away)

Read the message above the graph. **No visible connections in this direction** describes a completed view. **Source coverage is incomplete** means the relevant source has a gap. The explorer counts gaps elsewhere in the division separately. An empty view with a continuation invites you to check more results. [B9](02-behaviours.md#b9--explain-empty-incomplete-and-partial-results)

A snapshot or workspace change can invalidate a continuation. If results are out of date, choose **Reload** to start again at page one for the same focus and direction. If a request failed temporarily, use **Retry** to repeat it and keep your loaded connections. [B10](02-behaviours.md#b10--recover-from-an-invalidated-or-failed-continuation)

## Add or maintain context as a manager

Read-only accounts can inspect both origins and read their history. Contribution controls appear for managers. Choose **Inspect focus** to find **Add note** and **Create relationship**. Available notes and workspace relationships have edit and remove actions in their details. [B11](02-behaviours.md#b11--show-contribution-actions-to-managers)

To create a relationship, choose the endpoints by owning source and check **From entity** and **To entity**. Use a published relationship type or select **Custom meaning** and enter a label and explanation. After saving, use **View saved relationship** or **View saved note**. Other readers see the record the next time they load the relevant graph or details. [B12](02-behaviours.md#b12--create-and-discover-workspace-contributions)

To revise or remove a record, open its details and use its edit or remove action. Confirm the removal when asked. Changes appear in a fresh reader view, and removed records remain in contribution history. The explorer refreshes page one while retaining the current focus and direction. [B13](02-behaviours.md#b13--reflect-edits-and-removals-in-a-fresh-view)

If saving fails, keep the browser tab open. Review the latest revision next to your draft and choose **I reviewed the current revision** before submitting a conflict again. For an expired session, choose **Sign in again** from the editor. If an endpoint is unavailable or write access was denied, check the selected endpoints or your workspace role before retrying. [B14](02-behaviours.md#b14--preserve-a-rejected-draft)

Notes, workspace relationships, and their history are stored in the runtime workspace. Published concepts and Turtle source files are read-only in the explorer. [B15](02-behaviours.md#b15--keep-repository-inputs-read-only)
