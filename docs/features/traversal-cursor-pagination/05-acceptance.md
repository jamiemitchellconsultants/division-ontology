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

## Results

Each row records: walkthrough result / executable result, the date, and the person who did the walkthrough.

| Behaviour | Local build | Home lab |
| --- | --- | --- |
| B1 | pending | pending |
| B2 | pending | pending |
| B3 | pending | pending |
| B4 | pending | pending |
| B5 | pending | pending |
| B6 | pending | pending |
| B7 | pending | pending |
| B8 | pending | pending |
| B9 | pending | pending |
| B10 | pending | pending |
| B11 | pending | pending |
| B12 | pending | pending |
| B13 | pending | pending |
| B14 | pending | pending |
| B15 | pending | pending |
| B16 | pending | pending |
| B17 | pending | pending |
| B18 | pending | pending |
| B19 | pending | pending |
