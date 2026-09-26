# Division ontology

A stewarded shared vocabulary and a complete, sourced catalog of ontology concepts from related repositories. This private repository currently includes [JointBooking](https://github.com/jamiemitchellconsultants/JointBooking). Its first approved shared term is `StaffId`.

The shared vocabulary lives in [`core/ontology.ttl`](core/ontology.ttl). Each source remains authoritative for its local concepts. [`sources.json`](sources.json) names participating repositories; [`links/`](links/) holds explicit relationships to shared terms. The generated [`dist/catalog.md`](dist/catalog.md) is the readable view. [`dist/catalog.json`](dist/catalog.json) is a structured catalog; [`dist/combined.nq`](dist/combined.nq) preserves every source triple in named graphs. Both record source provenance.

The first catalog contains **73 JointBooking concepts, 13 rules, and one explicit shared link**, built from JointBooking commit `6957928a6c9054372dda915b359f63b73969ebee`. The division core contributes one approved term. Concepts with similar names remain separate unless an explicit link relates them. Source commits in the generated catalog show the current published snapshot.

## Minimal instructions for an agent

- **Add `owner/repo` to division ontology** — the agent checks that repository's committed Turtle ontology, proposes its registration for steward review, and scheduled sync adds all its concepts after approval.
- **Sync division ontology now** — the agent triggers the manual sync workflow and reports its result.

The normal sync runs every 30 minutes and maintains one generated catalog PR. Verified changes to unlinked source concepts can merge automatically after the [one-time setup](docs/automation-setup.md). Shared meaning and existing linked-concept changes still require a division steward.

## Build from a local checkout

Requires Node.js 20 or newer and a clean checkout of each source repository.

```bash
npm ci
npm test
npm run build -- --checkout jamiemitchellconsultants/JointBooking=/path/to/JointBooking
npm run check -- --checkout jamiemitchellconsultants/JointBooking=/path/to/JointBooking
```

`build` writes the three files in `dist/` after all sources and links validate. `check` compares them against the current source commits without writing. A failed build leaves the previous complete catalog in place. Omit `--checkout` to clone the repositories named in `sources.json` with your existing Git credentials; this repository stores no credential. A local checkout must be clean and have an `origin` matching its configured source URL. The ontology is read directly from the recorded commit.

## Contribute

See [GOVERNANCE.md](GOVERNANCE.md). In brief:

1. Propose a division-wide concept by pull request to `core/ontology.ttl`, with a definition and evidence from at least one source repository. A division steward reviews its meaning and name.
2. Add a repository to `sources.json` to include **all** of its ontology concepts. Give it a stable namespace and a Turtle path. Add a links file only for relationships that have been reviewed.
3. Use `exact` only when the local and shared concepts have the same meaning and canonical label. It emits `owl:equivalentClass`. Use `specializes` for a narrower local class; it emits `rdfs:subClassOf`. Matching labels alone create no link.
4. Run the tests, build and check locally. Commit authored changes in a reviewed PR. The sync workflow proposes generated output in its own PR after that merges.

## Current boundary

The division build validates the shared term and produces a combined catalog. It does not alter JointBooking or its CI. Making every participating repository's own ontology checker consume a pinned division-core snapshot needs a later Ontology Kit integration; the current kit checks one local document, and JointBooking has its own Turtle projection. Until then, division validation runs here when this catalog is built.

CI runs the code tests without cross-repository credentials. The scheduled sync uses a GitHub App to read private sources. Until the [App and branch-rule setup](docs/automation-setup.md) is complete, catalog refreshes remain manual and the existing owner review applies. The catalog is private because it includes all concepts and descriptions from its sources.
