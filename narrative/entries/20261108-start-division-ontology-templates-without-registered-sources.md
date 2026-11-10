---
date: 2026-11-08
slug: start-division-ontology-templates-without-registered-sources
title: "Start division ontology templates without registered sources"
summary: "Start the template with no registered source repositories or source links. Retain the existing division-owned core. Use a disposable workspace with synthetic sources for demonstrations and persistence testing."
kind: product
status: accepted
sequence: 2026-11-08T19:44:22.000Z
evidence: "https://github.com/jamiemitchellconsultants/division-ontology/pull/60; merge commit 99b9415aff2661c82cfee51ac91e78e69bd40866"
---

## Context

Each corporate division needs its own copy of this repository with a source inventory that it can steward. The existing default registration and link would carry JointBooking into every new copy, and the catalog builder currently rejects a source inventory with no entries.

## Decision

Start the template with no registered source repositories or source links. Retain the existing division-owned core. Use a disposable workspace with synthetic sources for demonstrations and persistence testing.

## Consequences

Each division registers its own sources. The catalog refresh follows this authored PR in a separate generated-only PR. Enable GitHub's template setting after the refreshed catalog is on main.

AI-Fingerprint: sha256:0b3e04f1226b
