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
