---
date: 2026-11-10
slug: make-the-repository-usable-as-a-division-template
title: "Make the repository usable as a division template"
summary: "Move the namespace and steward logins into a steward-governed `division.json` read by the build and the trusted merge policy, and provide a one-time `init-division` script that requires each division to choose its namespace explicitly."
kind: product
status: accepted
sequence: 2026-11-10T06:20:07.000Z
evidence: "https://github.com/jamiemitchellconsultants/division-ontology/pull/73; merge commit 0390404f46da096f4055b7ba7859c52a3fee53eb"
---

## Context

The repository is a template that each corporate division copies to steward its own catalog. Copies inherited this repository's steward logins in the merge policy, so a division could never approve its own first source registration. They also inherited this repository's core namespace, its Project Narrative and in-flight feature work, and references to JointBooking, a source the template no longer registers.

## Decision

Move the namespace and steward logins into a steward-governed `division.json` read by the build and the trusted merge policy, and provide a one-time `init-division` script that requires each division to choose its namespace explicitly. Keep the shipped C4 and workspace feature documentation in copies, because the deploy runbook and core ontology cite it, and remove only this repository's narrative, in-flight feature and planning history. Rejected: deriving stewards from CODEOWNERS (a PR could edit both together) and leaving the namespace shared by default (divisions could mint conflicting terms under one IRI).

## Consequences

A division can start from the template, run one command and onboard sources under its own stewards. Changing stewards or the namespace becomes a steward-approved change. The template itself can no longer register sources without removing the template guard test. Keeping the template's namespace in a copy remains possible, but only by passing it explicitly.

---

AI-Fingerprint: sha256:1d58baf5699b

🤖 Generated with [Claude Code](https://claude.com/claude-code)
