# Agents start here

Use this document when a person asks you to **“Create a division from this template”**. It takes you from that request to a created, initialised, and handed-over division repository. For everything after creation — adding sources, proposing shared terms, syncing — follow [AGENTS.md](../AGENTS.md), which is the canonical instruction file for this repository and every division copy.

Work in order. Do not skip the prompting step, and do not report a step done when it is only pending.

## 1 — Prompt first, create nothing yet

Before creating anything, ask the founder for the choices below and record every answer in a setup record (step 6 shows its shape). Keep the record in your report to the founder; commit it nowhere.

### Required choices — never defaulted, never invented

| Choice | Record |
| --- | --- |
| Target owner/name for the new repository | |
| Division namespace (the shared core’s IRI namespace) | |
| Steward logins (one or more GitHub logins) | |
| Confirmation that a public repository publishing source ontologies is intended | |

If any required answer is missing, stop and ask. Never invent a target owner/name, a division namespace, or steward logins, and never default one of these four.

### Optional setups — ask each by name, state its declined default

| Optional setup | If declined (the default) |
| --- | --- |
| Deploy the runtime knowledge workspace | Catalogue-only: no workspace is deployed. |
| An embedding/semantic-search provider (ask only when the workspace is accepted) | No semantic search: the catalogue and text search only. Similarity search is a discovery aid for human judgement; it never creates a link, an equivalence, or any other ontology relationship, and concepts that share a label stay distinct. |
| Set up the C4 impact pilot | Not set up: the C4 registries stay empty. |
| Granted-source onboarding via the dedicated source-read App | Steward-registered sources only: every source joins through a reviewed `sources.json` entry. |
| Enable auto-merge for verified catalogue pull requests | Catalogue pull requests wait for steward review. |

Record each answer as **accepted**, **declined**, or **defaulted — not answered**. If the founder will not answer an optional prompt, apply its declined default, record it as defaulted — not answered (never as accepted or declined), and continue. An enabling step for an optional setup runs only when its recorded answer is accepted.

The setup record holds choices and pending steps only. Never put credentials in a file or command argument, and never record a token, private key, or password in the setup record.

## 2 — Create and clone

With the required choices recorded, create the new repository from this template and clone it:

```bash
gh repo create <target owner/name> --template jamiemitchellconsultants/division-ontology --public --clone
```

GitHub’s **Use this template** button followed by a clone of the new repository is the equivalent manual route. Work in the new clone from here on. If creation fails, stop and report the failure; initialise nothing in the template repository itself.

## 3 — Initialise once, before any sources

In the new clone, exactly once, before any source is registered:

```bash
npm ci
npm run init-division -- --namespace <recorded division namespace> --steward <recorded steward login> [--steward <each further steward login>]
```

Then verify, and report any mismatch instead of passing over it:

- `division.json` holds the recorded namespace and steward logins.
- The shared core in `core/ontology.ttl` and every line of `.github/CODEOWNERS` use them — including this document’s line, so the new stewards own it.
- The Narrative is empty, `sources.json` registers no sources, and the catalog in `dist/` is rebuilt in the new namespace.

Review the resulting diff before any commit, and commit only when the founder asks.

## 4 — Automation and chosen setups

Follow [the automation setup](automation-setup.md) for the one-time GitHub App and branch-rule administration: the division-writer App, the dedicated source-read App when granted sources were accepted, their variables and secrets (set the source-read App’s client ID variable and private key secret together), the required checks, and — only when auto-merge was accepted — `DIVISION_AUTO_MERGE_READY` last, after the merge-policy gate is verified live.

For each accepted optional setup, follow its own canonical document instead of improvising here:

- Workspace: the workspace runbook in `docs/features/runtime-knowledge-workspace/05-runbook.md`, and [configure embedding search](adding-embedding-search.md) when an embedding provider was accepted.
- C4 impact pilot: the C4 material linked from the README’s “C4 impact pilot” section.
- Granted sources: the source-read App section of the automation setup.

Where a step needs a GitHub App creation or installation, a branch-protection or required-check change, a steward approval, or a private-source owner’s publication consent that you do not have, record the step as **pending**, name who must do it, and do not report the setup as complete. Never grant yourself App access.

## 5 — Verify the first sync

Trigger `Sync division catalog` with `workflow_dispatch`, or await the scheduled run, and report the workflow result or the generated catalog pull request. If a source or access step fails, fix its underlying cause before retrying; a failed run leaves the previously published catalog untouched.

## 6 — Hand over

Report the completed setup record to the founder: one row per required choice and optional setup, each optional setup shown as accepted, declined, or defaulted — not answered, plus every pending step and who must do it. Never report an optional setup simply as “skipped”.

Then point the founder onward, without restating those documents here:

- [AGENTS.md](../AGENTS.md) — ongoing agent requests (“Add `owner/repo` to division ontology”, “Join `owner/repo` to division ontology”, “Sync division ontology now”) and the repository’s review discipline.
- [GOVERNANCE.md](../GOVERNANCE.md) — shared terms, sources, links, and publication.
- [The automation setup](automation-setup.md) — App and branch-rule administration.
- The workspace runbook (`docs/features/runtime-knowledge-workspace/05-runbook.md`) — only when the workspace was accepted.

This document stays in the new repository, owned by its stewards, so the next agent asked to work on that division can find the same starting point.
