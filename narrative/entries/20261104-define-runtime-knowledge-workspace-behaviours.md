---
date: 2026-11-04
slug: define-runtime-knowledge-workspace-behaviours
title: "Define runtime knowledge workspace behaviours"
summary: "Retain contributions when endpoints disappear, mark them unavailable and exclude their relationships from default traversal while allowing explicit inclusion."
kind: product
status: accepted
sequence: 2026-11-04T08:05:08.000Z
evidence: "https://github.com/jamiemitchellconsultants/division-ontology/pull/34; merge commit 40bd7a68d7fb7440c9ced87634f3f1bfde4535e6"
---

## Context

Feature #25 combines a published knowledge baseline with persistent human-authored context and relationships. Its accepted vision left snapshot changes, contribution ownership, history, simultaneous edits, optional codification and missing JWT roles to analysis. These need precise observable outcomes before architecture can proceed.

## Decision

Retain contributions when endpoints disappear, mark them unavailable and exclude their relationships from default traversal while allowing explicit inclusion. Any ManageKnowledgeGraph user may edit or remove contributions, with preserved history and explicit review of stale edits. Contributors may choose published relationship types or supply custom meanings. Codification associations are explicit and preserve both provenances. Access requires a valid JWT with a workspace role; no-role tokens receive no implicit reader access.

Add PublishedKnowledgeSnapshot, KnowledgeAnnotation, RuntimeRelationship, KnowledgeContributionChange and CodificationAssociation with definitions sourced from the accepted vision and analyst decisions. Existing term IRIs, StaffId meaning, source registrations and reviewed links are preserved.

## Consequences

Architecture can work from the accepted behaviours once this PR is reviewed and merged. The external InternalOntology/C4 graph contracts and identifiers must be confirmed before implementation planning; pilot product, owner, wider-business reviewer and representative search examples are deferred to acceptance planning. The new shared terms require division-steward approval.

Generated catalog outputs were built, checked and reviewed locally. They are excluded from this authored PR; scheduled sync will propose the generated-only update after merge.
