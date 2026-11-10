---
date: 2026-11-10
slug: docs-amend-one-action-onboarding-vision-for-division-templates
title: "docs: amend one-action onboarding vision for division templates"
summary: "An owner joins one particular division by granting that division's own read-only App access. Joining must need no steward approval of the steward-governed source registration or division settings."
kind: product
status: accepted
sequence: 2026-11-10T06:42:54.000Z
evidence: "https://github.com/jamiemitchellconsultants/division-ontology/pull/77; merge commit 60fe6124e925009ba9dd6ed3076f7c9a546739d6"
---

## Context

The one-action source onboarding vision assumed a single division catalog with one preapproved read-only App. #73 made the repository a template: each division copy sets its own namespace and stewards in `division.json`, the merge policy requires steward approval for changes to `sources.json` and `division.json`, the template must ship with no registered sources, and each division creates its own App.

## Decision

An owner joins one particular division by granting that division's own read-only App access. Joining must need no steward approval of the steward-governed source registration or division settings. First visibility is demonstrated in a division created from the template, not in the template. The capability ships in the template. One App serving several divisions, and registering sources in the template itself, are out of scope.

## Consequences

The analyst works from a vision that matches the template model. Behaviours and acceptance must target a division copy, and admission must avoid the steward-gated files. Multi-division routing from a single grant is not designed for.

AI-Fingerprint: sha256:73f4718393ff

🤖 Generated with [Claude Code](https://claude.com/claude-code)
