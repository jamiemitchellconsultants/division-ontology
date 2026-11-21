# Traversal cursor pagination user guide

`POST /api/traverse` returns at most 100 paths per response. When a walk has more, the response gives you a cursor for the next page. This guide is for the three users in the [vision](01-vision.md#users):

- **Knowledge readers** with `ReadKnowledgeGraph` who explore a busy entity.
- **Agents and API clients** that page through results.
- **Knowledge contributors** with `ManageKnowledgeGraph` whose workspace relationships must stay visible.

The workspace UI still shows only the first page (UI work is out of scope), so paging is done through the API. The interactive docs at `/api/docs` and the spec at `/api/openapi/v1.json` contain ready-made first-page and next-page examples.

## Page through a traversal (B1–B4, B12)

1. Send the traversal as before, without a cursor:

   ```json
   { "start": { "sourceId": "fixture/ecommerce", "kind": "concept", "identifier": "https://example.test/ecommerce/ontology#Order" },
     "direction": "outgoing", "maxDepth": 1, "includeUnavailable": false }
   ```

2. If the response has `"nextCursor"`, send the **same body** with `"cursor"` set to that value.
3. Repeat until a response has no `nextCursor`. That page is the last one.

Concatenated in order, the pages contain every path exactly once, in the order an unpaged walk would produce them: breadth-first, and within each entity its published relationships before its workspace relationships. This holds for `outgoing`, `incoming` and `both`, for every depth from 1 to 8, and with or without `includeUnavailable`. A published step is identified by its `key`. A workspace step has a `runtimeId` and `"origin": "runtime"`, so the two stay distinguishable on every page. An unavailable endpoint keeps `endpointUnavailable: true` and its `lastKnownSnapshotId` on whichever page it appears.

Sending the same cursor twice returns the same page. A cursor never skips or repeats a path.

## Know when you are finished (B5, B6)

- **First page:** without a cursor, you get the same first 100 paths, entities, gaps and `truncated` value as before. The only change is that `nextCursor` is added when the walk continues. A walk that fits on one page is unchanged: `truncated: false` and no `nextCursor`.
- **`truncated: true`** means the walk is *not proven finished*. A cursor always comes with it. The next page can be short or even empty, because the server limits how much it examines per request. An empty page is not an error: keep following the cursor.
- **`truncated: false`**, with no `nextCursor`, means the walk is complete. The last page may contain no paths.

If your client reads only `truncated`, it can still tell whether it has seen everything.

## Render a page on its own (B7)

Each page's `entities` holds the label, provenance, availability and active annotations of every endpoint in that page's paths, both published and workspace-authored. An entity that appeared on an earlier page is repeated, so you can render each page without other requests.

## When a cursor stops working (B8–B11, B19)

Every cursor failure tells you to **restart the traversal from the first page**. Drop the cursor and send the original request again.

| Response | Cause | What to do |
| --- | --- | --- |
| `422`, `code: invalidCursor` | The cursor is damaged or truncated, or was sent with a different `start`, `direction`, `maxDepth` or `includeUnavailable` from the request that issued it. | Restart from page one with the parameters you meant. |
| `409`, `code: staleCursor` | Since the cursor was issued, a new published snapshot was activated, or **any** annotation or relationship in the workspace was created, edited, removed or codified, even one unrelated to your walk. | Restart from page one. The new walk reflects the current data. |
| `422`, `code: invalidTraversal`, “Restart with a smaller maxDepth or a more specific start entity” | The rest of this walk is too large to fit in a cursor. | Lower `maxDepth` or start from a more specific entity. |

**For contributors:** any edit you save makes every reader's open cursor stale. Readers part-way through a long walk then have to start again. Saving several changes together, before readers resume, means fewer restarts.

Cursors are opaque. Do not build or edit one yourself. A changed cursor is rejected, or gives you a walk you could have requested directly, and never more.

## Follow the `next` link (B13–B15)

For agents and generated clients:

- Every response that has another page carries `Link: </api/traverse>; rel="next"`. The last page omits it. The spec's `x-agent-discovery.linkRelations` maps `next` to `traverseGraph`, and its `200` response declares a `next` link to the same operation, next to `entityDetail` and `relationshipDetail`.
- The link names the endpoint and the body carries the cursor. To follow it, `POST` to the link target with your original request body plus `"cursor": <nextCursor>`. Stop when the `next` link is gone.
- The `traverseGraph` operation in the spec documents the optional `cursor` request field, the `nextCursor` response field, `422 invalidCursor`, `409 staleCursor`, and named `firstPage` and `nextPage` request examples.

## Cost and safety (B16–B18)

Each page costs about the same however deep into the walk it is, and filtered-out unavailable relationships do not make a page slower without limit. The server examines at most 1,000 relationships per request. Paging is read-only: it never changes published records, workspace contributions or any Turtle file.
