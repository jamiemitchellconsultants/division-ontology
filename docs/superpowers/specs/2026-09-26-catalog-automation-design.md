# Division catalog automation — design

**Date:** 2026-09-26
**Status:** Approved by the user for implementation planning.

## Purpose and scope

Make the division catalog update itself when a configured repository's ontology changes, while keeping division stewards responsible for shared meaning. Work is confined to `jamiemitchellconsultants/division-ontology`. `JointBooking` and future source repositories need no new workflow or agent file for this release. All concepts in each configured source remain included. A second repository is not assumed or registered before its identity is known.

The intended human interaction after one-time setup is a short agent request such as **“Add `owner/repo` to division ontology”**. The agent discovers the source's committed Turtle path and namespace, adds it to `sources.json`, builds the catalog and opens a reviewed PR. Routine source revisions require no prompt. A source without a deterministic Turtle representation needs an explicit import adapter or a source-side Turtle projection before it can join; the agent must report that condition rather than guess a conversion.

## Alternatives considered

1. **Central scheduled sync (selected):** A workflow in this repository polls `sources.json` every 30 minutes and offers `workflow_dispatch` for an immediate run. It requires one GitHub App installation and no source-repository changes. The schedule is a reconciliation loop, so missed or delayed runs are harmless.
2. **Source-triggered dispatch:** Each source sends `repository_dispatch` after ontology changes. This is faster, but requires a workflow or webhook in every source, outside the chosen scope. It can be added later without changing the builder.
3. **Agent-only refresh:** Agents run `npm run build` when asked. This needs less setup but leaves freshness dependent on human prompts.

GitHub documents [scheduled and manual workflow triggers](https://docs.github.com/en/actions/reference/workflows-and-actions/events-that-trigger-workflows) and [GitHub App tokens for cross-repository access](https://docs.github.com/en/apps/creating-github-apps/authenticating-with-a-github-app).

## Source access and sync

An installed GitHub App can read configured private source repositories and write branches and pull requests in this repository. The App installation is limited to participating repositories plus `division-ontology`. Its private key is stored as an Actions secret, with its client ID as a variable. The workflow mints separate short-lived tokens: a read-scoped token for source clones and a write-scoped token restricted to `division-ontology` for PR creation. A single App is simpler to set up but has latent Contents write permission on every repository in its installation; organizations requiring strict separation should use two Apps with separate read and write installations. Source-clone credentials live only in environment variables; they are never written into tracked files, Git configuration, command arguments or generated output. Tokens are not exposed to pull-request code.

The sync workflow runs on the default branch under a concurrency group. It obtains the App token, installs pinned dependencies, invokes the existing builder against the current configured source refs, and checks the generated output. If nothing changed, it exits without a PR or notification. If source access, parsing, namespace validation or link validation fails, it leaves `dist/` on `main` unchanged and fails visibly with the source ID and cause. The next scheduled run retries after the source is repaired. `workflow_dispatch` provides an immediate retry without typing a command.

When output changes, the workflow creates or updates one bot-owned PR on `automation/catalog-refresh`, restricted to the three `dist/` files. It records source commits and concept counts in the PR body. The body carries a correct `AI-Fingerprint:` footer after the final bot commit, as required by `AGENTS.md`. Repeated runs update that PR rather than opening duplicates. The PR is never labeled `narrative-required` for a purely mechanical refresh.

## Review and merge policy

The bot may merge only a PR whose diff contains exactly `dist/catalog.json`, `dist/catalog.md` and/or `dist/combined.nq`, whose outputs match a fresh deterministic build from the configured source refs, and whose linked local concepts have not changed their meaning-bearing statements since the last catalog. The link guard compares label, definition, RDF types and all triples directly about each linked local IRI. A conservative rule also routes changed source rules associated with a linked label to review. New unlinked concepts and changes to unlinked source concepts remain source-owned and can merge automatically.

A required, trusted policy workflow runs from the default branch, treats the PR checkout as data, and does not execute PR code or expose the App key to it. For a generated-only PR it validates the exact output and link guard before reporting success. For every other PR it requires approval by `@jamiemitchellconsultants` at the current head commit. `core/ontology.ttl`, `links/`, `sources.json`, workflows, policy code and governance files therefore retain steward review. A linked-concept drift PR remains open for steward judgment; an agent can then propose a corrected link or shared term in a separate reviewed change. No model-generated similarity score authorizes a merge.

The current blanket code-owner approval and branch protection would block mechanical auto-merge. One-time setup replaces that blanket requirement with the required policy check, PR-only merging and ordinary CI checks, while protecting review-sensitive files through the policy and CODEOWNERS. The bot is not granted a general bypass. This repository setting change is a separate, explicit administrative step; until it is complete, bot PRs will still be created but wait for the existing owner approval. GitHub's [protected branch](https://docs.github.com/en/repositories/configuring-branches-and-merges-in-your-repository/managing-protected-branches/about-protected-branches) and [auto-merge](https://docs.github.com/en/pull-requests/how-tos/merge-and-close-pull-requests/automatically-merging-a-pull-request) controls provide the merge gate.

## Agent onboarding and minimal instructions

`AGENTS.md` and the README will include a provider-neutral playbook for Claude, Codex and Copilot. The expected user prompt is **“Add `owner/repo` to division ontology.”** The agent should inspect that repository, find its canonical Turtle ontology, identify the namespace and default ref, add one source entry, build and check against clean source checkouts, inspect the generated diff, and open a PR with the required narrative sections and AI fingerprint. The agent proposes links only with evidence of matching meaning; no link is required for onboarding. The source registration PR needs steward approval because all of that repository's concepts will become visible in the division catalog. A person must add a newly participating private repository to the reader App installation; an agent cannot grant itself that access. Once merged and accessible, scheduled sync includes the source automatically.

The playbook also gives **“Sync division ontology now”** as the manual fallback: trigger `workflow_dispatch`, then inspect the resulting PR or failure. No one should manually copy ontology terms into the division repository.

## Verification and release

Tests cover no-change idempotency, a changed source commit, multiple configured sources, output-only PR classification, linked-term drift, malformed source failure, and credential-free local operation. Workflow static checks validate trigger, permissions, allowed output paths and absence of secrets in command arguments. A local build and check against the clean `JointBooking` checkout must pass. A live manual workflow run after the App is configured must produce either no PR when current or one valid generated-only PR when a source changes. Branch protection must require the trusted policy check and CI before bot auto-merge is enabled.

The code and workflow can be merged before the App is installed; scheduled runs must fail clearly with setup instructions and must never publish incomplete output. The one-time App installation, Actions secret and branch-rule change are documented and verified separately. Until they are complete, the repository remains usable with the existing manual build and review process.
