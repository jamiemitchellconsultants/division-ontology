# 0007 — Deploy the workspace on Mac, home lab and AWS

- **Status:** accepted
- **Date:** 2026-11-07
- **Feature:** [#25](https://github.com/jamiemitchellconsultants/division-ontology/issues/25) ([architecture](../features/runtime-knowledge-workspace/03-architecture.md))

## Context

Development needs a complete Docker setup on a Mac. LocalAI operates the home lab. The AWS design favours managed services and Lambda where suitable, with the PostgreSQL database deployed in Docker. AWS verification is limited to simulation while an account is unavailable.

## Decision

Ship a self-contained Mac Compose environment and application-owned home-lab artefacts that LocalAI installs and operates. Describe AWS in Terraform with static hosting, Lambda for the API and bounded background work, managed S3 and Bedrock, and Docker PostgreSQL on a private EC2 instance with persistent EBS storage.

Preserve the PostgreSQL journal across redeployments. Test a rebuild from the journal and referenced bundles as described in [ADR 0008](0008-rebuild-workspace-from-snapshots-and-events.md). Validate Terraform and the application integration through simulation for AWS. Run the actual application and database contracts on Mac and the home lab.

## Consequences

The application and database image are consistent across deployments. The AWS database remains an operated service and requires upgrades. Lambda connection counts, job duration, private network access and provider wiring require checks before any live rollout. AWS simulation can establish configuration and adapter behaviour within the simulated boundary. Backup and disaster recovery are tracked in [follow-up #53](https://github.com/jamiemitchellconsultants/division-ontology/issues/53).

## Alternatives considered

- Use a managed PostgreSQL instance for AWS. The design needs Apache AGE, which is absent from the published RDS and Aurora extension lists consulted for this phase.
- Run all application services on the database host. This would expand host responsibilities and couple application releases and scaling to the database.
