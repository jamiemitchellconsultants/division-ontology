# Governance

## Shared terms

Division stewards are the GitHub logins listed in `division.json`; changing that list or the core namespace needs a current steward's approval. A division steward approves changes to `core/ontology.ttl`. A proposal identifies the concept's intended scope, canonical label, definition, source evidence, and consequences for existing links. The first approved concept is `StaffId`, the enterprise-wide staff number. A term can be added with one source when its definition is genuinely wider than that repository; a second repository is not invented to justify it.

A shared IRI is stable. Renaming a label does not silently change its meaning. A material change to meaning needs a new term or an explicit correction in the pull request. The `owl:versionInfo` value changes with a reviewed core release; consumers should pin the release when local checks are integrated.

## Sources and links

Every configured repository contributes all named concepts from its committed Turtle ontology. Source repositories own their definitions. This repository records their source commit in the generated catalog and keeps each source in its own RDF named graph.

A repository added to the division's read-only source App with a committed `.division-ontology.json` is a granted source. It joins without steward approval and contributes all its concepts, and its import status is published in the catalog. A granted source can never carry links or change shared terms, and a namespace that overlaps the core's or another source's is rejected, so the existing holder is unaffected. A repository that is also registered in `sources.json` is one source, and its steward registration governs.

A link is a reviewed assertion, never an automatic conclusion from a matching name. `exact` asserts class equivalence and therefore requires equal canonical labels; `specializes` asserts that the local class is narrower than the shared class. Incorrect links are corrected by a new pull request. Similarity suggestions may inform review but do not block or create links.

## Publication

Build and check against clean source checkouts. Authored changes to the core, source list, links or policy need a reviewed PR. A scheduled or manually triggered sync proposes `dist/` in a separate generated-only PR. It merges automatically only after a trusted fresh build matches every output file and existing linked concepts retain their meaning-bearing statements. Changes to linked concepts wait for current division steward approval. If a source fails to build, the last published catalog remains intact and the sync run fails for investigation.

The catalog records source commits so readers can judge freshness. This repository is public, so the catalog publishes every concept and description of each registered or granted source. A private source may join only when its owner accepts that its ontology becomes public. New private sources need an administrator to expand the reader App installation; source registration never gives the agent repository access by itself.
