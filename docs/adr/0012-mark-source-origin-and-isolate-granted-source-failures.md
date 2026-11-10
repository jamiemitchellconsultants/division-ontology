# 0012 — Mark source origin, isolate granted-source failures and classify their refreshes as automatic

- **Status:** accepted
- **Date:** 2026-11-10
- **Feature:** #6 (docs/features/one-action-source-onboarding/)

## Context

B2 says a refresh that only adds, updates or changes the status of granted sources merges without a steward, while anything touching reviewed links, linked concepts or the core still needs review. B3 keeps steward registration working and makes the registration win when a repository is also granted. B15 says one failing source must not hold back the rest. B16 rejects a new claimant of an already-used namespace.

Today `classifyRefresh` treats any change to the `(id, namespace)` list of `catalog.sources` as "source registration changed", which forces review. `buildCatalog` throws on the first concept IRI shared by two owners, so a single bad source fails the whole build. Both are right for steward-registered sources and wrong for granted ones.

## Decision

1. **Every catalog source records its `origin`:** `registered` (in `sources.json`) or `granted`. A repository in both is one `registered` source (U7). Links remain possible only for registered sources, because `linksPath` exists only in `sources.json`. A link naming a granted source fails the build (U14).
2. **Registered sources stay strict.** An unreadable or invalid registered source fails the build as it does now, which is what keeps the catalog tied to steward-approved sources.
3. **Granted sources are validated one at a time in a fixed order** (by source id). A failure becomes that source's status row (ADR 0011) and does not stop the others (B15).
4. **Namespace conflicts favour the existing holder.** A granted source whose namespace equals the core namespace, a registered source's, or one already used by an earlier published or already-validated granted source is `invalid` with `namespace-conflict`. A published holder is identified from the committed catalog, so a later claimant can never displace it (B16). Two new claimants in the same run are decided in id order and reported.
5. **`classifyRefresh` compares only registered sources** in its source view, and treats changes to granted sources, their concepts, their graphs and their status rows as automatic. The existing checks for reviewed links, linked concepts and the division core are unchanged, as is the rule that any change outside generated `dist/` needs review. The merge policy therefore needs no new rule: a generated-only refresh the classifier calls automatic is already allowed to merge without a steward.

## Consequences

- A source owner's grant, a source's new commit and an access removal can all merge without a person, which is what the vision asks for. The merge-policy rebuild keeps this honest: a candidate that does not match a fresh build from the live installation still fails.
- Granted sources cannot alter core terms or links, so the automatic path cannot change meaning that stewards have reviewed.
- Because granted sources join without review, the catalog now trusts the owner's own claim of a namespace. The conflict rule, the in-namespace check on every concept, and the fact that only stewards can add links limit the effect, but a granted source can still publish false definitions inside its own namespace. This is the same trust placed in registered sources' content, and the status table names the repository and commit for anyone judging it.
- The classification code gains the `origin` field. Existing catalogs without it read as `registered`.

## Alternatives considered

- **Review every granted refresh.** Keeps one rule, but it re-adds the steward step the feature removes.
- **Fail the build on any granted source error** (as registered sources do). Simple, but one owner's bad commit would stall every source (B15).
- **Treat a namespace conflict as first-come-first-served by commit time.** Needs timestamps (ADR 0011) and lets an owner displace a holder by timing, so the committed catalog is the arbiter instead.
