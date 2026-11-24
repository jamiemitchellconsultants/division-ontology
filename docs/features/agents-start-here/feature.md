# Agents start here

**Feature:** [#189](https://github.com/jamiemitchellconsultants/division-ontology/issues/189) (lite mode)

## Product owner — vision

**Status:** Accepted by the product owner on 24 November 2026.

### Problem

This repository is a GitHub template for starting a new division, but an agent asked to create a division from it has no instructed path for doing so. The human-facing README describes clicking **Use this template** and running `init-division`, and `AGENTS.md` — the canonical agent instruction file — only covers operating a division that already exists (“Add `owner/repo`…”, “Join `owner/repo`…”, “Sync division ontology now”). An agent asked to start a division must improvise the template-creation and cloning steps, and nothing requires it to ask the user about the template’s optional setups. It can silently skip them, enable them without consent, or present a partial setup as complete.

### Users

- **Division founder:** the person asking an agent to create a new division repository from this template. They own the choices the agent cannot invent: target repository, namespace, stewards, and which optional setups they want.
- **Coding agent:** the agent carrying out the setup on the founder’s behalf. It needs one discoverable, ordered path from template creation to a verified, handed-over division, with explicit stop conditions.
- **Division steward (incoming):** one of the logins the founder names. They inherit the created repository and need the setup record — what was enabled, what was declined, and what still needs their or an administrator’s action — to govern it.

### Outcomes and measures

1. **An agent can create and initialise a division without inventing steps.** Measure: following only the new instructions, an agent creates a scratch division from the template, initialises it, and the result matches what `init-division` guarantees — the founder’s namespace and stewards in `division.json`, the core and CODEOWNERS rewritten, an empty Narrative, no registered sources, and a rebuilt catalog. A walkthrough that needs knowledge the instructions do not supply is a failure.
2. **The agent prompts for every setup choice instead of assuming it.** Measure: before proceeding, the agent asks about and records answers for the required choices (target owner/name, division namespace, steward logins, and confirmation that a public repository publishing source ontologies is intended) and for each optional setup: deploying the runtime knowledge workspace versus catalogue-only; an embedding/semantic-search provider if the workspace is deployed; the C4 impact pilot; granted-source onboarding via the dedicated source-read App versus steward-registered sources only; and enabling auto-merge for verified catalogue pull requests. For each optional setup the prompt states the default when declined. No optional setup is enabled, or reported as skipped, without the user’s recorded answer.
3. **Nothing is claimed done that needs someone else’s access.** Measure: where a step needs GitHub App creation or installation, branch-protection changes, steward approval, or a private-source owner’s publication consent, the instructions make the agent stop, hand off, and record the item as pending. The agent never invents a namespace, steward, or ontology content, and never puts credentials in files or command arguments.
4. **The result points onward instead of duplicating rules.** Measure: the instructions end by directing the agent and founder to the existing canonical homes — `AGENTS.md` for ongoing agent requests, `GOVERNANCE.md` for shared-term and link decisions, the automation setup for App and branch-rule administration, and the workspace runbook if the workspace was chosen — and restate none of their rules. Semantic similarity is described only as a discovery aid, never as automatic linking or equivalence.
5. **A division copy keeps the instructions.** Measure: the document survives `init-division` in a fresh copy (it is not template history removed during initialisation), and the copy’s stewards own it through CODEOWNERS like `AGENTS.md`.

### Scope

- An agent-facing “Agents start here” instruction document covering, in order: preconditions and the prompting step (Outcome 2); creating the repository from the template and cloning it; initialising it once, before any sources are registered; reviewing the resulting diff; completing or handing off the one-time automation setup; verifying the first sync; and handing over with a record of what was enabled, declined, and left pending.
- The prompting step itself: the questions to ask, the default for each declined optional setup, and where the agent records the answers before proceeding.
- Making the document discoverable from the existing canonical entry points (`AGENTS.md` and the README’s agent instructions) with pointers, without duplicating their content.
- Tests or checks that pin the document’s presence, its prompting requirement, and its survival in an initialised division copy.

### Out of scope

- Creating a real division, registering real sources, or granting any GitHub App access as part of this feature. The document instructs those steps; performing them for a live division is the founder’s separate request.
- New setup automation. The document uses the existing template mechanism and `init-division`; writing a creation script or changing `init-division` behaviour is not in scope (a gap found there is parked as a side issue).
- Choosing an embedding model, workspace deployment target, or any other optional setup for a particular division. The document makes the agent ask; the founder decides.
- Changing catalogue, builder, merge-policy, or workspace behaviour, and any form of automatic semantic linking or equivalence from similarity.
- Duplicating or replacing `AGENTS.md`, `GOVERNANCE.md`, or the automation-setup document.

### Research

- **The template mechanism is real but human-addressed.** GitHub reports this repository as a template (`isTemplate: true`). [README.md](../../../README.md) “Start a new division from this template” gives the human steps: **Use this template**, `npm ci`, `npm run init-division -- --namespace … --steward …`, review and commit, then the one-time automation setup.
- **Agent instructions start after creation.** [AGENTS.md](../../../AGENTS.md) is canonical (all other tool instruction files are thin pointers to it), and its “Short agent requests” plus the README’s “Minimal instructions for an agent” cover only adding/joining a source and syncing an existing division. `.agents/skills/` contains only the `feature-*` persona skills; there is no creation skill or agent request phrase for instantiating the template.
- **Initialisation is scripted and checkable.** [scripts/init-division.mjs](../../../scripts/init-division.mjs) refuses a copy with registered sources, rewrites the namespace in the core, `division.json`, and CODEOWNERS, empties the Narrative, removes template history and the template-only tests, and rebuilds `dist/`. [test/init-division.test.mjs](../../../test/init-division.test.mjs) already asserts these effects on a scratch copy, which Outcome 1’s walkthrough can build on.
- **Optional setups are documented but scattered.** The workspace is optional (a division can run catalogue-only); embedding search is optional and the catalogue and C4 pilot work without a model ([docs/adding-embedding-search.md](../../../docs/adding-embedding-search.md)); the dedicated source-read App enables granted sources while a single-App setup still serves registered ones; and catalogue auto-merge stays off until `DIVISION_AUTO_MERGE_READY` is set after the merge-policy gate is verified ([docs/automation-setup.md](../../../docs/automation-setup.md)). Nothing today makes an agent ask about any of them.
- **Guardrails the document must not blur.** Similarity may inform human link review but never creates links or equivalence ([GOVERNANCE.md](../../../GOVERNANCE.md)); registering or granting a private source publishes its ontology in this public repository, so owner consent is a stop condition, and an agent cannot grant itself App access ([AGENTS.md](../../../AGENTS.md)).

## Analyst — behaviours

**Status:** Accepted by the user on 24 November 2026.

**Basis:** the accepted vision above, the product-owner handoff on #189, and the analyst decisions recorded with the user below. Each numbered outcome matches the vision. Scenarios specify observable behaviour of the agent following the instructions, and of the instructions themselves; they do not prescribe components or file layout.

### Behaviours

#### Outcome 1 — An agent can create and initialise a division without inventing steps

- **B1 — Create from the template.** **Given** the required choices of B5 are recorded, **when** the agent follows the instructions, **then** it creates the target repository from this template and clones the new repository, working in that clone from then on. If template creation fails, no initialisation is attempted in the template repository itself.
- **B2 — Initialise once, before any sources.** **Given** a fresh clone with no registered sources, **when** the agent initialises it with the recorded namespace and steward logins, **then** initialisation runs exactly once, before any source is registered, and the agent reviews the resulting diff before any commit.
- **B3 — Verify the initialised division.** **Given** initialisation has run, **when** the agent checks the result, **then** `division.json` holds the recorded namespace and stewards, the core and CODEOWNERS use them, the Narrative is empty, `sources.json` registers no sources, and the catalog is rebuilt in the new namespace. Any mismatch is reported, not passed over.
- **B4 — Instructions-only walkthrough.** **Given** a tester with only the instructions and a set of recorded choices, **when** they follow the instructions against a scratch copy, **then** the initialised result of B3 is reached without knowledge the instructions do not supply. A step that needs outside knowledge is recorded as a failure of the instructions. The walkthrough creates no real GitHub repository (see U3).

#### Outcome 2 — The agent prompts for every setup choice instead of assuming it

- **B5 — Prompt for required choices before creation.** **Given** a request to create a division, **when** the agent starts, **then** it asks for and records the target owner/name, the division namespace, the steward logins, and confirmation that a public repository publishing source ontologies is intended. No repository is created until all four are recorded; the agent invents none of them.
- **B6 — Prompt for each optional setup, with its declined default.** **Given** the required choices are recorded, **when** the agent reaches the prompting step, **then** it asks about each of these by name, stating the default that applies if declined: deploying the runtime knowledge workspace (default: catalogue-only); an embedding/semantic-search provider, asked when the workspace is chosen (default: no semantic search, existing catalogue and text search only); the C4 impact pilot (default: not set up; the empty registries stay empty); granted-source onboarding via the dedicated source-read App (default: steward-registered sources only); and enabling auto-merge for verified catalogue pull requests (default: catalogue pull requests wait for steward review). A prompt that names a setup without its declined default does not meet this behaviour.
- **B7 — Record every answer before proceeding.** **Given** the prompts of B5 and B6, **when** the agent proceeds past the prompting step, **then** each choice stands recorded as accepted, declined, or defaulted (B8), and that record is what the handover of B9 shows. An enabling step for an optional setup runs only when its recorded answer is accepted.
- **B8 — An unanswered prompt defaults, visibly.** **Given** the user declines to answer an optional-setup prompt, **when** the agent continues, **then** it applies the declined default of B6, records that item as “defaulted — not answered” (never as accepted or declined), and continues. Required choices of B5 are not defaulted: setup stops until they are answered.
- **B9 — Hand over the setup record.** **Given** setup reaches handover, **when** the agent reports completion, **then** the report lists every required choice, every optional setup as accepted, declined, or defaulted, and every pending hand-off of B11. An optional setup is never reported simply as “skipped”.

#### Outcome 3 — Nothing is claimed done that needs someone else’s access

- **B10 — Stop rather than invent.** **Given** a required choice is missing, or template creation or initialisation reports an error, **when** the agent cannot proceed from recorded answers, **then** it stops, reports what is missing or failed, and creates or changes nothing further. A failed run leaves any previously published catalog untouched.
- **B11 — Hand off administrator steps as pending.** **Given** a chosen setup needs GitHub App creation or installation, branch-protection or required-check changes, steward approval, or a private-source owner’s publication consent, **when** the agent reaches that step without the needed access, **then** it records the step as pending, names who must do it, and does not report the setup as complete. The agent never grants itself App access and never puts credentials in files or command arguments.
- **B12 — No secrets in the record.** **Given** the setup record of B9, **then** it contains choices and pending steps only — no token, private key, or password, in the record or in any file the agent wrote during setup.

#### Outcome 4 — The result points onward instead of duplicating rules

- **B13 — Point to the canonical homes.** **Given** a reader finishes the instructions, **when** they look for how to operate the division, **then** the instructions direct them to `AGENTS.md` for ongoing agent requests, `GOVERNANCE.md` for shared-term and link decisions, the automation setup for App and branch-rule administration, and the workspace runbook if the workspace was chosen. The instructions restate none of those documents’ rules.
- **B14 — Similarity is discovery only.** **Given** any mention of semantic search or similarity in the instructions, **then** it is described as a way to find records for human judgement. No instruction tells the agent to create a link, an equivalence, or any ontology relationship from a similarity match, and same-labelled concepts are described as staying distinct.

#### Outcome 5 — A division copy keeps the instructions

- **B15 — The document survives initialisation.** **Given** a fresh copy initialised as in B2, **when** its files are inspected, **then** the “Agents start here” document is still present and unchanged in substance (it is not removed as template history).
- **B16 — The copy’s stewards own the document.** **Given** the initialised copy of B15, **when** its CODEOWNERS is inspected, **then** the document is assigned to the copy’s recorded steward logins, as `AGENTS.md` is.

### Uncertainties

| ID | Question | Decision or explicit deferral | Basis |
| --- | --- | --- | --- |
| U1 | What makes “prompted” testable? | B5–B9: named prompts, each stating its declined default; every answer recorded as accepted, declined, or defaulted before any enabling step; the record is shown at handover. | Product-owner handoff question; analyst decision. |
| U2 | What if the user will not answer an optional prompt? | Apply the declined default, record “defaulted — not answered”, and continue (B8). Required choices are never defaulted. | Decided by the user in analyst review. |
| U3 | What evidence proves Outcome 1? | A scratch-copy initialisation following only the instructions, asserting B3; no real GitHub repository is created (B4). | Decided by the user in analyst review. |
| U4 | Is the optional-setup list complete? | The five setups of B6 are the required minimum for this feature, closed from the current template inventory. A later optional feature amends the list by changing these behaviours. | Analyst decision from the documented optional features. |
| U5 | Is new setup automation needed? | No. Documenting the existing template mechanism and `init-division` is the feature’s scope; a gap needing script changes is parked as a side issue. | Vision scope and out-of-scope. |
| U6 | Are pointers from AGENTS.md and README discoverable enough, without a new skill? | Yes, for this feature: `AGENTS.md` is the canonical file every agent tool is pointed at. A dedicated skill is deferred as out of scope. | AGENTS.md pointer structure; vision scope. |
| U7 | Does this feature introduce domain terms? | No new division knowledge concept is introduced, so `core/ontology.ttl` is unchanged. | Vision and Terms below. |

### Terms

This feature introduces no domain term, so `core/ontology.ttl` is unchanged. The words below are vocabulary of the instructions, not ontology concepts:

- **Setup record:** the recorded required choices, each optional setup’s state (accepted, declined, or defaulted — not answered), and the pending hand-offs, handed over at the end of setup (B7, B9).
- **Declined default:** the state that applies when an optional setup is declined or defaulted, as stated in its prompt (B6, B8).
- **Pending hand-off:** a step recorded as needing a named person’s access or approval before the setup can be called complete (B11).

Existing terms used unchanged: division steward (`division.json`), granted source, and the reviewed link kinds `exact` and `specializes`.
