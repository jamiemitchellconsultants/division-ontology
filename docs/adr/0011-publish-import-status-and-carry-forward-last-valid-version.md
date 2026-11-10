# 0011 — Publish import status in the catalog and carry the last valid version forward

- **Status:** accepted
- **Date:** 2026-11-10
- **Feature:** #6 (docs/features/one-action-source-onboarding/)

## Context

B10–B14 need a per-source status in the catalog that readers can also see (U4). B11 and B14 need the previous valid version to stay in the catalog when a later commit is invalid or access is gone. In the latter case the source can no longer be read, so the earlier version cannot be rebuilt from the source. B17 needs no refresh when nothing changed, and B10 forbids credentials in reasons.

Today `dist/` is a pure function of `division.json`, `core/`, `links/`, `sources.json` and the source commits. The generated files are committed and are the only record of what was published.

## Decision

1. **Status is generated data in `dist/`.** `dist/catalog.json` gains an `importStatus` array, and `dist/catalog.md` gains a status table. A row has `source`, `status` (`current`, `invalid`, `not configured` or `access removed`), `attemptedCommit`, `publishedCommit` and, for failures, `reason` with a `code` and a short `detail`. Rows exist only for granted sources. There are no timestamps or attempt counters, so an unchanged state produces identical bytes and no refresh (B17).
2. **The committed `dist/` is an input to the build.** For each granted source the builder takes its last published state from the committed `dist/catalog.json` and `dist/combined.nq`: the commit, the namespace and the source's named graph. When a source's current head is invalid, or its access is gone, the builder re-emits that recorded graph and re-projects its concepts from it. It is marked stale in `sources` and in the status row. The build stays deterministic: the same committed `dist/`, the same live source state and the same configuration produce the same files.
3. **Content failures become status. Infrastructure failures fail the sync.** A missing or invalid configuration, an unreadable ontology path, invalid Turtle, a concept outside the namespace or a namespace conflict are facts about the source's commit, so they become `invalid` or `not configured` rows. A network error, rate limit, API error or incomplete listing says nothing about the source, so it fails the run and leaves `dist/` untouched.
4. **Reasons use a closed set of codes with bounded, sanitised detail.** Codes include `config-missing`, `config-invalid`, `ontology-unreadable`, `ontology-invalid-turtle`, `concept-outside-namespace`, `no-concepts`, `namespace-conflict` and `access-removed`. The detail is built from validator values (a path, a line number, the conflicting source id), never from raw Git output or exception text, and is checked for token patterns before it is written.
5. **A never-published source that loses access, or is removed from the installation, produces no row** (B14). A published source that loses access keeps its row as `access removed`.

## Consequences

- The App owner and readers see the same table, with no extra service.
- `verifyRefresh` in the merge policy must copy the base `dist/` into its temporary build root, or its fresh build could not reproduce a carried-forward source. The branch-must-be-up-to-date rule keeps base and candidate aligned.
- Rolling back `dist/` also rolls back what is carried forward. That is the correct behaviour for a generated record, but it means a reverted catalog PR can briefly show a granted source's older state until the next sync.
- Readers of `catalog.sources` (the C4 pilot's snapshot loader, the workspace importer) now see granted and stale sources. Each source carries `origin` and `stale` so they can tell. The developer must check what these consumers do with a source they cannot clone.
- Concepts of a stale source stay visible and unchanged, so a reader can use `status` and `publishedCommit` to judge freshness.

## Alternatives considered

- **Keep status outside the repository** (a workflow summary or issue). It would not be visible to readers (U4) and could not carry a source's previous version forward.
- **Drop a source's concepts when it turns invalid.** Simpler, but it contradicts B11 and B14 and turns one bad commit into a catalog regression for every consumer.
- **Store last-good snapshots in a separate cache.** Adds state with no review trail. The committed `dist/` already is the reviewed record.
- **Record timestamps of each attempt.** Gives App owners a heartbeat but makes every sync change the files, which breaks B17.
- **Turn infrastructure errors into `invalid` rows.** Would show a source as failing, and could replace published state, because of a temporary GitHub fault.
