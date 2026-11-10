# Adding a repo with an internal ontology to division-ontology

Three prompts for onboarding a new source repository, one per actor. Replace
`OWNER/REPO` with the actual GitHub `owner/repo` before use.

## Fast path: grant the division's read-only App

A source owner can join a division without a pull request here or any steward approval:

1. Commit `.division-ontology.json` to the root of the repository's default branch: `{ "schemaVersion": 1, "ontologyPath": "docs/ontology.ttl", "namespace": "https://your.namespace/ontology#" }`. The path must name the committed Turtle ontology and the namespace must be the one its concepts use.
2. Add the repository to the division's source-read App (its selected repositories). You need administrator access to the repository, and your organisation must allow you to add the App.
3. Wait for the next sync, or ask the division's App owner to run `Sync division catalog`.
4. Look for the repository's row under **Import status** in the division's `dist/catalog.md`. `current` means its concepts are published at that commit; `invalid`, `not configured` or `access removed` explain what to fix.

The steps below remain for steward-registered sources, for reviewed links to shared terms, and for repositories that cannot be granted. A granted source cannot carry links or change shared terms, and a namespace already used by the core or another source is rejected.

## Who needs what access

| Persona | Access to `division-ontology` | Access to `OWNER/REPO` | What only they can do |
| --- | --- | --- | --- |
| Division steward (prompt 1) | Admin / code-owner | None required | Approve the source registration PR and any linked-concept change; merge to protected `main` |
| Non-admin collaborator (prompt 2) | Fork or write access, enough to open a PR | Read/write (non-admin) | Inspect the ontology, draft `sources.json` entry, open the registration PR — cannot approve it or grant App access |
| Source repo owner/admin (prompt 3) | None required | Admin | Commit/confirm the canonical Turtle ontology; if `OWNER/REPO` is private, approve or install the `jamiemitchellconsultants` sync GitHub App on it |

## 1. To type here, in division-ontology

```
Add OWNER/REPO to division ontology.
```

This is the documented magic phrase (AGENTS.md "Short agent requests"). The
agent here will inspect `OWNER/REPO` for a canonical committed Turtle
ontology, namespace and default ref, add one entry to `sources.json`, build
and check against a clean checkout, and open a PR with the source
registration (following the Narrative and AI-fingerprint rules). It stops and
asks the steward to approve, and it cannot itself grant the sync App read
access if `OWNER/REPO` is private — that needs step 3 below.

## 2. For a non-admin collaborator on the source repo to type into their agent

```
I have write access to OWNER/REPO but not admin rights, and no special
access to jamiemitchellconsultants/division-ontology beyond being able to
open a pull request there. Follow the onboarding steps in
jamiemitchellconsultants/division-ontology's AGENTS.md to add OWNER/REPO as a
division ontology source:

1. Inspect OWNER/REPO for its canonical committed Turtle ontology file,
   namespace, and default ref. If no deterministic Turtle file exists, stop
   and tell me — don't invent ontology content.
2. Add one entry to division-ontology's sources.json for OWNER/REPO (id,
   url, ref, ontologyPath, namespace, and linksPath if you're also proposing
   a link). Include all of its concepts; don't curate a subset. Only propose
   a link with genuine evidence of matching meaning, and put it in a
   separate reviewed PR if you do.
3. Build and check against a clean checkout of OWNER/REPO and review the
   generated catalog and diff locally.
4. Open a PR against division-ontology containing only the source
   registration (not generated dist/ files), following its Narrative and
   AI-fingerprint protocols from AGENTS.md.
5. In the PR description, explicitly flag that (a) a division steward still
   needs to approve the registration, and (b) if OWNER/REPO is private,
   someone with App administration access on that repo still needs to add it
   to the sync App installation — don't attempt either of those yourself.
```

## 3. For the source repo owner/admin to type into their agent

```
jamiemitchellconsultants/division-ontology wants to onboard this repo
(OWNER/REPO) as a source. Two things need my admin access here, which you
can't get by yourself:

1. Confirm OWNER/REPO has a canonical, committed Turtle ontology file (per
   our usual DevNotes/Ontology convention — a human-readable ontology doc
   plus a .ttl/.jsonld serialisation) with a stable namespace, and tell me
   its path, namespace, and the ref (branch) it should be read from. If it
   doesn't exist yet, tell me rather than fabricating one.
2. If OWNER/REPO is a private repository, division-ontology's sync reads it
   via a GitHub App owned by jamiemitchellconsultants, installed on
   "selected repositories." That installation can only be granted by someone
   with admin access on this repo/account — that's me, and it has to happen
   through GitHub's UI, not through you. Tell me exactly what to do: whether
   I need to approve an installation request from jamiemitchellconsultants,
   or go to the App's install page myself and add OWNER/REPO to its
   repository list. Don't attempt to install or authorize anything yourself.

Report back the ontology path/namespace/ref so it can go into
division-ontology's sources.json, and confirm once the App has repo access.
```
