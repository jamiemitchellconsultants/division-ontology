# 0026 — Record the actual source graph version in workspace bundles

- **Status:** accepted
- **Date:** 2026-11-24
- **Feature:** #175 (docs/features/c4-schema-2-source-graph-import/)

## Context

The workspace bundle producer uses the same C4 snapshot loader and currently writes 1 for every available source graph. The runtime reader also accepts only 1 in that field. Enabling schema 2 imports without making those two points agree would mislabel valid input or prevent a faithfully labelled bundle from being read. ADR 0004 requires the bundle to record source graph versions. [Pinned bundle decision](0004-import-and-activate-pinned-knowledge-snapshots.md)

## Decision

Use the validated graph state's source manifest version for the existing per-source `graphSchemaVersion` field. Allow 1 and 2 for available graphs in the workspace bundle reader and retain null for unavailable graphs. Keep the bundle envelope at version 1, including the same fields, pinned raw objects, published projection shape and digest checks.

Use mixed source-version fixtures to verify the producer and reader agreement and keep coverage for replaying existing version 1 bundles. The C4 snapshot remains the boundary for inspecting the new design node fields.

## Consequences

A bundle can state the exact source graph schema that was imported and use the established activation path with the compatible reader. Existing bundles continue to read under the same envelope. Existing installations should update the reader before publishing a bundle containing schema 2 sources. The feature can be accepted with disposable fixtures and the pinned JEPA-Speech trial.

## Alternatives considered

- Keep writing 1 for schema 2 source graphs. The provenance would contradict the imported manifest.
- Reject schema 2 sources at the workspace boundary. The same allowed catalog would then load through C4 and fail to bundle.
- Introduce a new bundle envelope or expand the workspace API. The version field and the raw pinned files already carry the information required by this change.
