---
date: 2026-11-04
slug: add-feature-lifecycle-workflow
title: "Add feature lifecycle workflow"
summary: "Add the feature lifecycle CLI, persona skills, and PR gate workflow."
kind: product
status: accepted
sequence: 2026-11-04T16:48:17.000Z
evidence: "https://github.com/jamiemitchellconsultants/division-ontology/pull/45; merge commit abd8fac538d49302e13782782ff508fd110bb12d"
---

## Context

The feature phases are tracked in GitHub, but main lacks the lifecycle CLI required by the installed feature skills.

## Decision

Add the feature lifecycle CLI, persona skills, and PR gate workflow.

## Consequences

Agents can check phase gates locally, and pull requests receive the same gate check in CI.

AI-Fingerprint: sha256:b2a1c1c95f98
