---
date: 2026-11-10
slug: fix-remove-the-template-only-tests-from-a-new-division-copy
title: "fix: remove the template-only tests from a new division copy"
summary: "`init-division` removes `test/template.test.mjs` and `test/init-division.test.mjs` from a division copy, next to the template history it already removes."
kind: product
status: accepted
sequence: 2026-11-10T17:41:26.000Z
evidence: "https://github.com/jamiemitchellconsultants/division-ontology/pull/96; merge commit e1e7e650410cb73e44b4123bf42adef0a6cb4f40"
---

## Context

In the deployed acceptance, sync run 42865502599 succeeded, refresh PR #3 was finalized with a real fingerprint, auto-merge was requested and `merge-policy` passed. Its `test` check then failed (run 42865572813, https://github.com/jamiemitchellconsultants/onboarding-acceptance-division/actions/runs/42865572813) on `test/template.test.mjs` and `test/init-division.test.mjs`, which assert the repository has no sources. A division inherits them, and `init-division` removed the template's history and narrative but not these tests. Because `test` is a required check, the refresh PR stayed blocked. The same flaw already existed for steward-registered sources, but this feature makes a division with granted sources the normal case.

## Decision

`init-division` removes `test/template.test.mjs` and `test/init-division.test.mjs` from a division copy, next to the template history it already removes. The `init-division` test now copies both into its fresh copy and asserts they are gone, and the README says what is removed and what to do for a division made earlier. The alternatives were making the tests skip outside the template, which leaves dead tests in every division, or relaxing them, which stops them guarding the template.

## Consequences

A new division's required `test` check no longer fails when a source joins. An existing division deletes the two files in a pull request, as the README now says. The template keeps both tests and still enforces that it ships empty. Any future test that asserts template-only facts must be added to `templateOnlyTests` in `scripts/init-division.mjs`. Nothing else about behaviour, the catalog or the architecture changes.

AI-Fingerprint: sha256:50ca021c6ca6

🤖 Generated with [Claude Code](https://claude.com/claude-code)
