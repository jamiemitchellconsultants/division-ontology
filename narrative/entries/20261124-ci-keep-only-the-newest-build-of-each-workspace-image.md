---
date: 2026-11-24
slug: ci-keep-only-the-newest-build-of-each-workspace-image
title: "ci: keep only the newest build of each workspace image"
summary: "GHCR keeps only the newest build of each workspace image. Retention is set by version count (3, one complete build) rather than by age. That keeps the newest build intact however long it has been since the last publish."
kind: product
status: accepted
sequence: 2026-11-24T12:59:24.000Z
evidence: "https://github.com/jamiemitchellconsultants/division-ontology/pull/182; merge commit ec3d34368716a82545da829b13043e61aff59c57"
---

## Context

Every image publish added 3 versions to each of 5 packages, and none were ever removed. The repository is about to become public along with its images, and older builds served no deployment.

## Decision

GHCR keeps only the newest build of each workspace image. Retention is set by version count (3, one complete build) rather than by age. That keeps the newest build intact however long it has been since the last publish. Keeping several builds for rollback was rejected because nothing deploys an older `sha-` tag. A rollback is a rebuild from the earlier commit.

## Consequences

Older `sha-` tags stop being pullable as soon as a newer build publishes, so anything pinned to an older tag must move to the newest one. If the build ever produces a different number of versions per build, `min-versions-to-keep` must change with it. Examples are adding a platform or turning off provenance attestations. Otherwise pruning could delete part of the newest build or keep part of an older one.

---

AI-Fingerprint: sha256:59bc3960bc9b

🤖 Generated with [Claude Code](https://claude.com/claude-code)
