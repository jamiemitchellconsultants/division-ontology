# Division Ontology First Release Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Publish a versioned shared term and a complete, provenance-preserving catalog from JointBooking's ontology.

**Architecture:** A Node CLI reads a steward-owned core and clean source checkouts through the N3 RDF parser. It validates explicit links, projects a readable catalog, and writes deterministic JSON, Markdown and N-Quads only after validation.

**Tech Stack:** Node.js 20+, ESM, `n3` pinned to 2.7.12, `node:test`, Git.

**Spec:** `docs/superpowers/specs/2026-09-26-division-ontology-design.md`

## Global Constraints

- The initial source is `jamiemitchellconsultants/JointBooking`, path `docs/ontology.ttl`, namespace `https://jointbooking.internal/ontology#`.
- The division namespace is `https://github.com/jamiemitchellconsultants/division-ontology#`.
- The first approved shared term is `StaffId`, with one explicit `exact` link.
- Source repositories are read only; no credential is stored here.
- On validation failure, existing generated outputs remain untouched.

## Review Focus

- A label collision between distinct source IRIs remains two concepts; test in Task 2.
- A source checkout with uncommitted changes is rejected; test in Task 1.
- An exact link with a different label fails; test in Task 2.
- A failed build leaves old outputs byte identical; test in Task 3.
- A changed source commit makes `check` report stale outputs; test in Task 3.

---

### Task 1: Parse and identify sources

**Files:** `src/rdf.mjs`, `src/sources.mjs`, `test/sources.test.mjs`, `package.json`, `package-lock.json`

**Interfaces:** `parseTurtle(text, baseIRI)` returns RDF quads; `loadSources(manifestPath)` returns validated source descriptors; `resolveSource(source, checkoutOverrides)` returns `{directory, commit, cleanup}` and rejects dirty checkouts.

- [ ] Write `test/sources.test.mjs` covering malformed Turtle, duplicate IDs, namespace mismatch and dirty checkouts.
- [ ] Run `node --test test/sources.test.mjs`; observe failure for missing functions.
- [ ] Implement the exact interfaces above, use `execFileSync` for Git, and pin `n3@2.7.12`.
- [ ] Run `node --test test/sources.test.mjs`; require all tests pass.
- [ ] Commit the task.

### Task 2: Catalog and alignments

**Files:** `src/catalog.mjs`, `test/catalog.test.mjs`, `core/ontology.ttl`, `sources.json`, `links/JointBooking.json`

**Interfaces:** `buildCatalog({coreQuads, sourceQuads, sources, links})` returns `{catalog, quads}`; `loadLinks(path)` returns validated exact/specializes links. The catalog includes source and core concepts, relations, rules and source commits.

- [ ] Write `test/catalog.test.mjs` for complete concept inclusion, duplicate labels with distinct IRIs, valid `StaffId` link, missing link targets and mismatched exact labels.
- [ ] Run `node --test test/catalog.test.mjs`; observe failure for missing functions.
- [ ] Implement the catalog and link loader; add the approved core term, source manifest and initial link.
- [ ] Run `node --test test/catalog.test.mjs`; require all tests pass.
- [ ] Commit the task.

### Task 3: Deterministic outputs and CLI

**Files:** `src/output.mjs`, `src/cli.mjs`, `test/cli.test.mjs`, `.gitignore`

**Interfaces:** `renderOutputs(catalog, quads)` returns a map of the three generated filenames to strings; `runCli(argv, options)` runs `build` or `check` with repeatable `--checkout owner/name=/path` overrides.

- [ ] Write `test/cli.test.mjs` for stable output, stale check, source change, and unchanged output after failure.
- [ ] Run `node --test test/cli.test.mjs`; observe failure for missing functions.
- [ ] Implement rendering and CLI. Render fully before writing a temporary output directory; replace `dist` only after success.
- [ ] Run `node --test test/cli.test.mjs`; require all tests pass.
- [ ] Commit the task.

### Task 4: Pilot and delivery

**Files:** `dist/catalog.json`, `dist/catalog.md`, `dist/combined.nq`, `README.md`, `AGENTS.md`, `.github/workflows/ci.yml`

**Interfaces:** `npm test` runs every test; `npm run build -- --checkout jamiemitchellconsultants/JointBooking=/path` builds from a clean local checkout; `npm run check -- --checkout ...` verifies current output.

- [ ] Add concise usage, stewardship, provenance and credential guidance; add CI that runs `npm ci` and `npm test` only.
- [ ] Run `npm test` and build against the local JointBooking checkout; inspect counts and the approved link.
- [ ] Run `npm run check -- --checkout jamiemitchellconsultants/JointBooking=/Users/jamesmitchell/RiderProjects/JointBooking`; require success.
- [ ] Commit the first generated catalog and repository documentation.
