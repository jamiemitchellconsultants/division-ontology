# 0021 — Host the explorer in the existing workspace

- **Status** accepted
- **Date** 2026-11-21
- **Feature** [#145](https://github.com/jamiemitchellconsultants/division-ontology/issues/145)

## Context

Readers arrive from search or entity details and need to inspect evidence while keeping their place. Managers need the existing versioned contribution workflow. The workspace already provides authorized traversal, detail, session, contribution, and history operations. Published provenance contains pinned evidence coordinates. Repository URLs are separate from those records. [Behaviours B1, B6–B7, B11–B15](../features/knowledge-graph-explorer/02-behaviours.md)

## Decision

Add an authorized route to the existing Blazor WebAssembly app and a detail panel in that route. Use `WorkspaceClient` for all API interactions. Reuse the existing provenance, contribution, history, and draft capabilities. Existing API roles and validation govern every write. Store successful contributions and their history only in the runtime workspace. The published baseline comes from pinned, read-only repository inputs.

Use the existing browser configuration for an optional map of source IDs to public or private GitHub repository URLs. For a mapped source, build an HTTPS link only from a trusted repository URL and the evidence's owning revision and repository-relative path. Keep the citation visible when a link cannot be formed. The destination applies its own repository access rules. The configuration contains only repository locations.

## Consequences

The explorer ships in the existing web artifact and uses the same sign-in, runtime store, and publication boundary as the rest of the workspace. The developer needs a UI composition seam for the editor and a return route for reauthentication. The existing history remains the record of committed changes, including removals and edits. Evidence links can be enabled per source through web configuration, and the inspector can display citations with the default configuration.

## Alternatives considered

- **A separate graph app.** This would duplicate navigation, identity integration, contribution handling, and deployment wiring.
- **A graph-specific service or repository writer.** The current read and contribution operations already cover the behaviours. Adding a write path to repositories would violate the read-only input boundary.
- **Derive source URLs from source IDs.** The ID names a source and can also represent a local fixture or a repository on a different host. Explicit configuration gives the inspector a verified base for links.
