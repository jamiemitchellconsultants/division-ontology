# 0010 — Declare source import settings in a dedicated committed file

- **Status:** accepted
- **Date:** 2026-11-10
- **Feature:** #6 (docs/features/one-action-source-onboarding/)

## Context

A granted source has no entry in `sources.json`, so the importer needs the ontology path and namespace from the source repository itself (B1, B9, B13). U10 asks which file and format carry them, coordinating with InternalOntology, whose installer already writes `ontology.config.json`.

That file's `ontologyPath` is `docs/ontology.md`, the generated Markdown view, not the Turtle ontology that the catalog imports. The copy read for this phase has no namespace field. Reusing the name would give one field two meanings.

## Decision

The SourceImportConfiguration is a JSON file named **`.division-ontology.json` at the root of the source repository's default branch**:

```json
{
  "schemaVersion": 1,
  "ontologyPath": "docs/ontology.ttl",
  "namespace": "https://example.org/ontology#"
}
```

- `ontologyPath` is a relative path that stays inside the repository and names a regular committed blob, not a symlink. `namespace` is an HTTP IRI. The importer applies the same checks `sources.json` entries get today (`loadSources`, `readSourceOntology`), including that concepts lie inside the namespace.
- Unknown keys, a missing key, a wrong `schemaVersion` or a file over 16 KiB make the configuration invalid.
- The file and the ontology are read from the same commit, the default-branch head the importer cloned, so a change to either arrives together (B8, B9).
- The importer pre-filters installation repositories by asking the Contents API whether the file exists on the default branch, so an organisation-wide installation (B5) does not clone every repository. The configuration it acts on is the one read from the clone.
- The InternalOntology installer should offer to write this file. That is a change in InternalOntology, not here, and is passed to the developer as a dependency. Until it ships, owners write the file by hand from the onboarding guide.

## Consequences

- The file belongs to this feature and can evolve with `schemaVersion` without touching InternalOntology's configuration schema.
- A source owner who already uses InternalOntology adds one small file once, in their own repository, as part of getting ready to be granted. This is not an action in the division repository.
- Two sources of truth exist for the Turtle path (the kit's own configuration, which names the Markdown, and this file). They describe different things, so the guide must say which is which.
- A repository that has not committed the file is `not configured` or ignored (U5), never guessed.

## Alternatives considered

- **Extend `ontology.config.json` with namespace and a Turtle path.** One file for the owner, but it overloads `ontologyPath`, couples releases of this repository to the kit's validator, and was not confirmed to accept the new fields.
- **Infer the path and namespace** from `docs/ontology.ttl` by convention. This breaks for any other layout and invents the namespace, which the repository forbids ("never infer equivalence from a matching name").
- **Entering the fields in the division repository.** That is the steward-gated registration the feature removes.
