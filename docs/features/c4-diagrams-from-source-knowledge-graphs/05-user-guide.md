# C4 impact pilot user guide

For the synthetic pilot, follow [the automated acceptance checks](05-acceptance.md#automated-reruns). For a published catalog, set up the pinned catalog, reviewed registries, source checkouts and output directory as described in [the runbook](05-runbook.md). Every command requires `--catalog-commit`, `--registry-commit`, one `--checkout owner/repo=/absolute/path` per catalog source, and `--output /absolute/output`.

## Start an assessment (B1)

Run `npm run c4 -- search` with the shared flags and `--query 'change description'`. Read `candidates.json`: match explanations and source identities are leads for your judgment, not impact conclusions. For a known IRI, use `--exact-iri <iri>`. An unmatched search and any gaps are not evidence of no impact.

## Follow paths (B2)

Run `npm run c4 -- paths` with shared flags and `--start owner/repo=<node-iri>` for each confirmed starting element. Examine `paths.json` step by step, including direction, assertions and evidence. A multi-step path is not a direct relationship; matching labels do not join sources.

## Record an assessment (B3)

Run `npm run c4 -- assessment-init` with shared flags, `--request-id <id>` and `--description-file <path>`. Edit the resulting `assessment.json`: add the architect, confirmed leads, rejected leads with reasons, likely impacts with their evidence references, and unresolved questions. Run `assessment-render` with the shared flags and `--input <assessment.json>`. When citing an inspected-code view, provide its digest in the impact's `viewIds` and pass its saved JSON with `--view-input <file>`; the option can be repeated. Share `assessment.md` for human review. The tool does not approve the request.

## Record pilot feedback (B4)

Create a separate JSON record containing `schemaVersion: 1`, the rendered assessment digest as `assessmentDigest`, `architect`, `reviewer`, `perceivedAccuracy`, `perceivedCompleteness`, `confidence`, and arrays named `usefulDiscoveries`, `suspectedMisses`, `falseLeads`, `examples` and `openQuestions`. Run `feedback-render` with shared flags, `--input <feedback.json>` and `--assessment-file <assessment.json>`; also supply cited views with `--view-input`. Review the generated `feedback.md` together. Record qualitative observations, including uncertainty; no numeric accuracy threshold is required.

## Explore views (B5–B9)

Run `npm run c4 -- view` with the shared flags, `--focus owner/repo=<node-iri>` and `--level context`, `container`, `component` or `code`. Read the corresponding JSON and Markdown reports. Context focuses on a system and supported relationships; container and component views retain their respective boundaries; code views show supported code. For inspected code, provide `--supplement <json>` with the source ID, path, line range, blob ID, component, role and reason, all referring to the pinned commit. Follow the selected element to its next supported level using the same snapshot. Missing detail is a visible gap, not a reason to discard available levels.

## Inspect and reproduce (B10–B12)

Inspect each item's source identity, commit, assertions and evidence in the report; inferred assertions are tentative. Distinct source identities stay distinct even when labels match. Preserve the catalog and registry commits and rerun the same focus and level to reproduce a view. Use a new published snapshot explicitly when source revisions change.

## Interpret gaps and inventory (B13–B15)

Read coverage and unresolved gaps alongside every view and assessment. An empty path does not establish that no dependency exists. Only catalog sources are participating repositories; a supported external system can appear as context without adding its repository to the catalog. A source may participate even when it has no C4 graph or some levels are unavailable.
