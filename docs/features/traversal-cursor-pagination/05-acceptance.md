# Traversal cursor pagination acceptance

**Feature:** [#119](https://github.com/jamiemitchellconsultants/division-ontology/issues/119) · **Phase:** [#125 — User docs & acceptance](https://github.com/jamiemitchellconsultants/division-ontology/issues/125) · **Behaviours:** [02-behaviours.md](02-behaviours.md) · **Runbook:** [05-runbook.md](05-runbook.md)

Each behaviour is accepted in two layers:

- **Walkthrough.** Numbered steps that the person acting as user follows by hand, each with an expected result.
- **Executable.** Automated tests of the same scenario:
  - `TraversalPaginationAcceptanceTests` in `runtime/RuntimeKnowledge.Tests`, run against a disposable Postgres. Filter with `--filter "Category=Acceptance"`, or one behaviour with `--filter "Behaviour=B7"`.
  - `deploy/workspace/verify-traversal-paging.mjs`, run against the real stack locally (`mac`) and on the home lab (`home-lab`).

## Walkthrough setup

**Local build.** Start a persistent local workspace with the synthetic fixture, then seed a busy node:

```bash
npm run demo:up
```

```bash
set -a; source "${TMPDIR:-/tmp}/division-workspace-demo/demo.env"; set +a; node deploy/workspace/verify-traversal-paging.mjs mac --seed-only
```

Open `http://workspace.localhost:$WORKSPACE_PORT/api/docs`. Sign in as `workspace-reader` (or `workspace-manager` where a step says manager) and open **traverseGraph**. "Send first page" means: send the `firstPage` example with `start` set to Ecommerce **Order** (`fixture/ecommerce`, `concept`, `https://example.test/ecommerce/ontology#Order`) and `maxDepth: 1`. "Send next page" means: send the same body with `cursor` set to the previous response's `nextCursor`. When you have finished:

```bash
node deploy/workspace/verify-traversal-paging.mjs mac --cleanup
```

```bash
npm run demo:down
```

**Home lab.** Do the same against the deployed origin. Set the fixture variables as described under **Operate** in the runbook, run `verify-traversal-paging.mjs home-lab --seed-only`, follow the walkthroughs in `/api/docs`, then run `--cleanup`.

The fixture has 105 seeded runtime relationships plus a few published ones from Ecommerce **Order**, so the outgoing one-hop walk spans two pages.

## Walkthroughs

### B1 — Continue a busy one-hop traversal
1. Send first page. **Expect** 100 paths, `truncated: true` and a `nextCursor`.
2. Send next page. **Expect** the remaining paths, no `nextCursor` and `truncated: false`.
3. Compare the step `key` or `runtimeId` values across both pages. **Expect** each of the 105 `paging-acceptance-…` relationships exactly once, published steps identified by `key` and workspace steps by `runtimeId`.

### B2 — Continue a multi-hop traversal across a branch boundary
1. Send first page with `maxDepth: 2`, then follow every `nextCursor` to the end.
2. **Expect** no path repeated, and the path lengths never decreasing from one path to the next across page breaks (breadth-first).
3. Run `--filter "Behaviour=B2"`. **Expect** the exact expected sequence of the branch-boundary fixture to pass.

### B3 — Each direction, with and without unavailable endpoints
1. Send first page with `start` set to Warehouse **Order** and `direction: incoming`, then follow cursors to the end. **Expect** all 105 seeded relationships once.
2. Repeat from Ecommerce **Order** with `direction: both`. **Expect** no path repeated.
3. Repeat step 1 of B1 with `includeUnavailable: true`. **Expect** the same paths as without it, because every endpoint is available in this fixture.
4. Run `--filter "Behaviour=B3"`. **Expect** a pass. That test covers unavailable endpoints keeping `lastKnownSnapshotId` on every page.

### B4 — Preserve path order across pages
1. Page through B1 twice. **Expect** identical sequences.
2. **Expect** every published path to come before the first workspace (`origin: runtime`) path.

### B5 — First page is today's behaviour plus `nextCursor`
1. Send first page. **Expect** the fields `snapshotId`, `paths`, `gaps`, `truncated`, `entities` and `nextCursor`, and nothing else.
2. Send first page from Accounting **Order** (a small walk). **Expect** `truncated: false` and no `nextCursor` field at all.

### B6 — `truncated` on continued pages
1. On every page in B1 and B2, **expect** `truncated` to be `true` exactly when `nextCursor` is present.
2. Run `--filter "Behaviour=B6"`. **Expect** a pass, including an empty non-final page and an empty final page.

### B7 — Entity context per page
1. On page two of B1, pick a path. **Expect** both its endpoints in that page's `entities`, with `label`, `provenance`, `available` and `annotations`, even though they also appeared on page one.

### B8 — Cursor binds the traversal parameters
1. Take page one's `nextCursor` and send it with `direction: both`. **Expect** `422`, `code: invalidCursor`, and a title telling you to restart from the first page.
2. Repeat with `maxDepth: 2`, with `includeUnavailable: true`, and with `start` set to Warehouse **Order**. **Expect** the same `422 invalidCursor` each time.

### B9 — Malformed cursors fail as `invalidCursor`
1. Send `cursor: "not a cursor"`. **Expect** `422 invalidCursor` with the restart instruction and no `paths`.
2. Send the first half of a real cursor. **Expect** the same.

### B10 — Snapshot refresh makes the cursor stale
1. Run `--filter "Behaviour=B10"`. **Expect** a pass: after a snapshot activation, the old cursor fails `409 staleCursor` with the restart instruction, and a fresh first page succeeds.
2. The deployed layer does not activate a different snapshot, because that would replace the fixture in use.

### B11 — Any workspace contribution change makes the cursor stale
1. Send first page and keep the `nextCursor`.
2. As **manager**, add an annotation to Accounting **Order** (not on the walk).
3. As reader, send next page with the kept cursor. **Expect** `409`, `code: staleCursor`, and the restart instruction.
4. Remove the annotation as manager.

### B12 — A valid cursor continues exactly once
1. Send next page twice with the same cursor. **Expect** identical paths both times.
2. Run `--filter "Behaviour=B12"`. **Expect** a pass over the 6,040-path wide fixture across more than 60 pages.

### B13 — `next` link while more pages exist

**Interpretation (accepted by the user, 21 November 2026):** B13 describes the relation as "the absolute HTTPS relation mapped to `traverseGraph`". The build uses the registered relation `rel="next"`, which `x-agent-discovery.linkRelations` maps to `traverseGraph`. That is the same convention inventory and history already use. Acceptance treats `rel="next"` as meeting B13, and the behaviour text is left unchanged.

1. Send first page and open the response headers. **Expect** `Link` to include `</api/traverse>; rel="next"`.
2. Send next page through to the final page. **Expect** no `rel="next"` on the final page, while the `entityDetail` and `relationshipDetail` links remain.

### B14 — Spec documents paging and keeps existing links
1. Open `/api/openapi/v1.json` and find `paths./api/traverse.post`. **Expect**:
   - the `cursor` request field and the `nextCursor` response field;
   - `422` listing `invalidCursor` and `409` listing `staleCursor`;
   - the `firstPage` and `nextPage` examples;
   - `200` links `next` (to `traverseGraph`), `entityDetail` and `relationshipDetail`.

### B15 — Following the link reaches the next page
1. Starting from page one, use only the `Link` target and the response's `nextCursor`, never a hand-made cursor, to reach the final page. **Expect** the same paths as B1.

### B16 — Late pages do not replay earlier pages
1. Run `--filter "Behaviour=B16"`. **Expect** a pass: every page of the wide walk examines at most 1,000 rows, the total examined equals the 6,040 rows in the walk (nothing replayed), and late pages cost no more than early ones.
2. In the deployed run, read the B16 line in the verifier output. **Expect** page latencies of the same order on every page. This is only a proxy, because examined-row counts are a test-only seam.

### B17 — Filtered-out unavailable relationships do not grow the work
1. Run `--filter "Behaviour=B17"`. **Expect** a pass: with 100 and with 1,200 filtered relationships, every page examines at most 1,000 rows, and the 99 available paths still arrive in order.

### B18 — Traversal stays read-only
1. Note the newest position in `/api/history`, page through B1, and submit the bad cursors from B8 and B9.
2. **Expect** the newest history position to be unchanged.
3. Run `--filter "Behaviour=B18"`. **Expect** a pass: the journal head, the published projection state and every repository Turtle file are unchanged.

### B19 — Remaining frontier exceeds the cursor size limit
1. Run `--filter "Behaviour=B19"`. **Expect** a pass: the walk fails `invalidTraversal` with “Restart with a smaller maxDepth or a more specific start entity”, and the spec lists `invalidTraversal` under `422`.
2. The deployed layer only checks that the spec lists `invalidTraversal`, because the real 262,144-character cap needs a much larger graph than the fixture.

## What the executable tests check

There are two layers:

- **Test suite.** `TraversalPaginationAcceptanceTests` runs against a throwaway database. Its **hub** fixture is one entity with 150 published relationships, `e000`–`e149`.
- **Real stack.** `verify-traversal-paging.mjs` signs in through Keycloak as the fixture manager and reader. It adds 105 relationships from Ecommerce **Order** to Warehouse **Order**, runs its checks, and removes them afterwards.

| Behaviour | Test suite: set-up, action, must be true | Real stack: must be true |
| --- | --- | --- |
| B1 | Hub plus one workspace relationship. Page through one hop. There are exactly two pages, and the first has 100 paths. Together they hold `e000`–`e149` then the workspace relationship, each once. The workspace one is not on page one. The last page has no cursor. | Every one of the 105 added relationships appears exactly once. Page one has 100 paths. The last page has no cursor and `truncated: false`. |
| B2 | A three-level tree: 2 branches, 3 sub-branches each, 30 leaves each. Page through depth 3. The paths equal the expected breadth-first list exactly, and page one ends part-way through a branch that page two continues. | A depth-2 walk repeats no path, and path length never goes down across page breaks. |
| B3 | 110 workspace relationships pointing *into* the hub. Incoming paging returns all 110 once. `both` returns 260 paths with none repeated, published first. A new snapshot then removes 51 leaves, which strands 120 workspace relationships. With unavailable endpoints included, every stranded step on every page is marked unavailable with the old snapshot id. Without them, only the 99 remaining published paths come back. | Incoming from Warehouse **Order** returns all 105 once. `both` repeats nothing. Turning on unavailable endpoints changes nothing, because all endpoints are available. |
| B4 | Two workspace relationships. Paging twice gives the identical sequence, with all 150 published paths before the two workspace ones. | All published paths come before the first workspace path. |
| B5 | Busy hub: page one is `e000`–`e099`, `truncated: true`, a cursor, and exactly the fields `snapshotId, paths, gaps, truncated, entities, nextCursor`. Exactly 100 paths with more to explore: a cursor, then an empty final page. A 99-path walk: `truncated: false` and no `nextCursor` field at all. | Page one has 100 paths, `truncated: true` and a cursor. |
| B6 | 99 published plus 30 filtered-out relationships, with a 10-row budget per page. `truncated` is true exactly when there is a cursor. At least one middle page is empty but still has a cursor. The final page is not truncated. All 99 published paths come through in order. | On every page, `truncated` matches whether there is a cursor. |
| B7 | A note on `leaf120` and a workspace relationship to `leaf005`. On every page, each endpoint of each path has an entity with label, provenance and availability. Page two carries `leaf120` with its note and `leaf005`. Page one does not carry `leaf120`. | Every page has label, provenance, availability and annotations for every endpoint on it. |
| B8 | Reuse page one's cursor with a different start, a never-published start, `both`, depth 2, and unavailable on. Each fails with 422 `invalidCursor` saying "Restart the traversal from the first page", with no paths. After a snapshot change it fails 409 `staleCursor` instead. | Changed direction, depth, unavailable option and start each fail 422 `invalidCursor` with the restart message. |
| B9 | Gibberish, half a real cursor, a fake cursor, and an oversized string. Each fails 422 `invalidCursor` with the restart message and no paths. | Gibberish and half a real cursor each fail 422 `invalidCursor`. |
| B10 | Activate a new snapshot. The old cursor fails 409 `staleCursor` with the restart message, and a fresh page one works. | Not run: it would replace the fixture. |
| B11 | For each of these, take a fresh cursor, make the change as manager, and the cursor then fails 409 `staleCursor`: create a note far from the walk, edit it, remove it, create a relationship, codify it against `e000`. | Adding a note to Warehouse **Order** makes an open cursor fail 409 `staleCursor`. The note is then removed. |
| B12 | A 6,040-path wide graph, more than 60 pages. Every cursor is followed, and the paths equal the expected list exactly. Sending the same cursor twice gives the same page. | Sending page one's cursor twice gives identical results, equal to page two. |
| B13 | The spec maps `next` to `traverseGraph`. Page one has `Link: </api/traverse>; rel="next"`. The final page has no `next` link but keeps `entityDetail`. | Every non-final page has the `next` link and the final page does not. |
| B14 | In both the served and the committed spec: the `cursor` and `nextCursor` fields, 422 `invalidCursor`, 409 `staleCursor`, a `firstPage` example without a cursor and a `nextPage` example with one, and `next`, `entityDetail` and `relationshipDetail` links with `next` pointing to `traverseGraph`. | The same in the served spec, plus 422 lists `invalidTraversal`. |
| B15 | A client that only follows the `Link` header target and the returned cursor reaches the last page in at most three calls, and collects `e000`–`e149` plus the workspace relationship. | Following only the `Link` headers reaches the last page. |
| B16 | Page the 6,040-path wide graph and count the rows examined per page. Every page examines at most 1,000. The total equals 6,040, so nothing is re-read. Late pages examine no more than early ones (allowing 101). | Records each page's response time. Not checked against a limit. |
| B17 | 99 published plus 100, then 1,200, filtered-out relationships. Every page examines at most 1,000 rows. The 99 published paths arrive in order, all on page one. | Not run. |
| B18 | Page through everything, then send a bad cursor and a mismatched one. The journal position, the published and contribution tables, and every `.ttl` file in the repository are unchanged. | The newest history position is unchanged after reader paging and the rejected cursors. |
| B19 | The wide graph with a tiny cursor limit. The walk fails `invalidTraversal` with "Restart with a smaller maxDepth or a more specific start entity", and the committed spec lists `invalidTraversal` under 422. | The served spec lists `invalidTraversal` under 422. |

## Results

Each row records: walkthrough result / executable result, the date, and the person who did the walkthrough.

**Local build: 21 November 2026, James Mitchell, on M5MBP.** By the user's standing instruction, every walkthrough with a known expected result is automated and reports only misses. The walkthrough result is the automated run that James Mitchell launched and confirmed, and the executable result is the same run. That run, on the demo workspace (`http://workspace.localhost:8087`, snapshot `cba0b960c07ba22fba44461caf5fc4e7fc76ad7ca03a93b27bc32e4bc0fdd821`), gave `PAGING: OK` and `TESTS: OK`, then removed the 105 walkthrough relationships. James Mitchell approved the executable tests from the plain-language summary above. Separately, `acceptance.sh local` passed repository (246), runtime (213), home-lab-config and local-browser, including the paging verifier.

| Behaviour | Local build | Home lab |
| --- | --- | --- |
| B1 | Pass / Pass (suite and real stack) | pending |
| B2 | Pass / Pass (suite and real stack) | pending |
| B3 | Pass / Pass (suite and real stack) | pending |
| B4 | Pass / Pass (suite and real stack) | pending |
| B5 | Pass / Pass (suite and real stack) | pending |
| B6 | Pass / Pass (suite and real stack) | pending |
| B7 | Pass / Pass (suite and real stack) | pending |
| B8 | Pass / Pass (suite and real stack) | pending |
| B9 | Pass / Pass (suite and real stack) | pending |
| B10 | Pass / Pass (suite only; real stack does not cover it) | pending |
| B11 | Pass / Pass (suite and real stack) | pending |
| B12 | Pass / Pass (suite and real stack) | pending |
| B13 | Pass / Pass (suite and real stack) | pending |
| B14 | Pass / Pass (suite and real stack) | pending |
| B15 | Pass / Pass (suite and real stack) | pending |
| B16 | Pass / Pass (suite and real stack) | pending |
| B17 | Pass / Pass (suite only; real stack does not cover it) | pending |
| B18 | Pass / Pass (suite and real stack) | pending |
| B19 | Pass / Pass (suite only; real stack does not cover it) | pending |
