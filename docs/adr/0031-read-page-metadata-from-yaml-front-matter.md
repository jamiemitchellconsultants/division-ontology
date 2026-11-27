---
status: accepted
date: 2026-11-26
feature: 198
---

# 0031 — Read page title and address from YAML front matter

Feature: docs/features/generalise-impact-assessment/

Amended 2026-11-27: the reader applies only to exported pages (B26, B38). Before this, it applied to every cited Markdown file.

## Context

Page evidence must show the exported page's title and its original Confluence address, read from the file at the pinned commit (B25). Missing metadata must be reported as a gap (B26). The kit graph schema and source repositories cannot change in this feature.

## Decision

A Markdown file cited by `selected-document` evidence is an exported page when line 1 is `---` and a later line closes the block with `---`. Any other cited file is an ordinary document, so the engine reads no metadata from it and reports no metadata gap for it (B38). For an exported page, the engine reads the front-matter block. The engine reads the scalar keys `title` and `confluence_url`, and the key names are constants in one place in the engine.

It parses only flat string scalars, with no anchors, tags or nested structures. A `confluence_url` is accepted only when it is an absolute `https` URL. If either key is missing, empty or invalid, the evidence shows its path and cited lines and the result reports `page-metadata-missing` for the page.

## Consequences

- Repository documents such as ADRs and feature documents, which have no front matter, are shown like code evidence and raise no gap.
- No change is needed to the kit or its graph schema. The Confluence export has to write these two keys.
- The metadata is pinned with the file's blob, so it cannot drift from the cited lines.
- A restricted parser keeps the attack surface small. The values are still escaped when rendered (Q4).

## Alternatives considered

- **A sidecar index file for each source.** This adds a second file to pin and to keep in step with the pages.
- **Take the first heading and a `Source:` line.** This depends on the page's content conventions and is fragile.
