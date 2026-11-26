---
status: accepted
date: 2026-11-26
feature: 198
---

# 0031 — Read page title and address from YAML front matter

Feature: docs/features/generalise-impact-assessment/

## Context

Page evidence must show the exported page's title and its original Confluence address, read from the file at the pinned commit (B25). Missing metadata must be reported as a gap (B26). The kit graph schema and source repositories cannot change in this feature.

## Decision

The engine reads a leading YAML front-matter block from each Markdown file cited by `selected-document` evidence. The block starts with `---` on line 1 and ends at the next `---`. The engine reads the scalar keys `title` and `confluence_url`, and the key names are constants in one place in the engine.

It parses only flat string scalars, with no anchors, tags or nested structures. A `confluence_url` is accepted only when it is an absolute `https` URL. If the block is missing, or either key is missing, empty or invalid, the evidence shows its path and cited lines and the result reports `page-metadata-missing` for the page.

## Consequences

- No change is needed to the kit or its graph schema. The Confluence export has to write these two keys.
- The metadata is pinned with the file's blob, so it cannot drift from the cited lines.
- A restricted parser keeps the attack surface small. The values are still escaped when rendered (Q4).

## Alternatives considered

- **A sidecar index file for each source.** This adds a second file to pin and to keep in step with the pages.
- **Take the first heading and a `Source:` line.** This depends on the page's content conventions and is fragile.
