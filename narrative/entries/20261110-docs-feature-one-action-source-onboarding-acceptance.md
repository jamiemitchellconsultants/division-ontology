---
date: 2026-11-10
slug: docs-feature-one-action-source-onboarding-acceptance
title: "docs(feature): one-action source onboarding acceptance"
summary: "Two acceptance layers per behaviour. Locally, a scripted harness creates two divisions with the real `init-division`, five local Git repositories and a file-backed stand-in for the App installation."
kind: product
status: accepted
sequence: 2026-11-10T18:44:02.000Z
evidence: "https://github.com/jamiemitchellconsultants/division-ontology/pull/91; merge commit c2663fd4fbe58c5cb9c5b54bcf08533b7d223549"
---

## Context

The build passes its own tests, but its GitHub client has only run against stubs, and the behaviours are only accepted when someone follows each one from outside and the same scenarios pass as executable tests, locally and in a real division with its own pair of Apps.

## Decision

Two acceptance layers per behaviour. Locally, a scripted harness creates two divisions with the real `init-division`, five local Git repositories and a file-backed stand-in for the App installation. The same actions drive both the numbered walkthrough and the ordered executable story in `test/acceptance/onboarding.test.mjs` (B1–B17). One extra check runs the real GitHub client over HTTP against a local API stand-in. In the deployed environment, `test/acceptance/onboarding-deployed.mjs` reads the published catalog and the live source heads and reports each discrepancy; granting and committing stay manual GitHub actions. At the participant's direction the commands compare actual with expected results themselves and stop only on an exception. Locally, `npm run --silent acceptance:onboarding` lists every check by name and prints one PASS line, or the expected and actual values of a failing check. Deployed, `onboarding-deployed.mjs run` commits to the test repositories and changes the App's repository list through `gh`, dispatches the division's sync, waits for it and its refresh PR, compares the published catalog with the live repositories, and stops at the first exception; `--dry-run` lists what it would do without calling GitHub. The deployed run has not been run against GitHub. The existing `rerun.mjs` takes a suite name, so `npm run acceptance:onboarding` mirrors `acceptance:c4`.

## Consequences

Nothing changes in the product in this PR. All six criteria on #14 are ticked. Organisation policy for adding the App (U12) is out of scope for this acceptance because it runs on a personal account; it is recorded as not tested. B4 needs a second division copy. There is no supported way to remove a granted source that has lost access; it stays in the catalog marked stale. That is recorded as a known limit, not fixed here.

AI-Fingerprint: sha256:494972783627

🤖 Generated with [Claude Code](https://claude.com/claude-code)
