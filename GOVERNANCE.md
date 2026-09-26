# Governance

## Shared terms

A division steward approves changes to `core/ontology.ttl`. A proposal identifies the concept's intended scope, canonical label, definition, source evidence, and consequences for existing links. The first approved concept is `StaffId`, based on JointBooking's description of the enterprise-wide staff number. A term can be added with one source when its definition is genuinely wider than that repository; a second repository is not invented to justify it.

A shared IRI is stable. Renaming a label does not silently change its meaning. A material change to meaning needs a new term or an explicit correction in the pull request. The `owl:versionInfo` value changes with a reviewed core release; consumers should pin the release when local checks are integrated.

## Sources and links

Every configured repository contributes all named concepts from its committed Turtle ontology. Source repositories own their definitions. This repository records their source commit in the generated catalog and keeps each source in its own RDF named graph.

A link is a reviewed assertion, never an automatic conclusion from a matching name. `exact` asserts class equivalence and therefore requires equal canonical labels; `specializes` asserts that the local class is narrower than the shared class. Incorrect links are corrected by a new pull request. Similarity suggestions may inform review but do not block or create links.

## Publication

Build and check against clean source checkouts. Commit `dist/` only after validation and tests pass. A failed build leaves the last complete output untouched. Review the source commit list before publishing so readers can judge freshness. This private repository is intended for the shared readership of all participating source repositories.
