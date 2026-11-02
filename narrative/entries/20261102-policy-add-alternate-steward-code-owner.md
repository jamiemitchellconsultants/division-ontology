---
date: 2026-11-02
slug: policy-add-alternate-steward-code-owner
title: "policy: add alternate steward code owner"
summary: "List `@james-mitchell-ba` as a code owner for the existing sensitive paths and recognize current-head approval from either `@jamiemitchellconsultants` or `@james-mitchell-ba` in `merge-policy`."
kind: governance
status: accepted
sequence: 2026-11-02T17:43:21.000Z
evidence: "https://github.com/jamiemitchellconsultants/division-ontology/pull/23; merge commit 16b803152d1a0406091e1295b99421f6d7437ddf"
---

## Context

PR #21 exposed a review deadlock: the trusted policy recognized only `@jamiemitchellconsultants`, which had authored the PR and could not approve its own work. An alternate account with write access is available for review.

## Decision

List `@james-mitchell-ba` as a code owner for the existing sensitive paths and recognize current-head approval from either `@jamiemitchellconsultants` or `@james-mitchell-ba` in `merge-policy`. An unresolved changes request by either steward continues to block the check.

## Consequences

Future authored and linked-concept changes can pass the trusted policy after approval by either listed steward account at the current head. CODEOWNERS review requests remain advisory to the required `merge-policy` check.
