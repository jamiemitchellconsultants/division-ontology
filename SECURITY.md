# Security policy

## Reporting a vulnerability

Please report security problems privately. Do not open a public issue or pull request.

Use GitHub's private vulnerability reporting: open the repository's **Security** tab and choose
**Report a vulnerability**. The report is visible only to the maintainers.

Include what you found, how to reproduce it, and what an attacker could do with it. You will
get an acknowledgement within a week, and we will agree a disclosure date with you once a fix is
ready.

## Scope

In scope:

- the catalog builder and checker in `src/` and `scripts/`
- the GitHub Actions workflows in `.github/workflows/`, in particular anything that lets a pull
  request from a fork run untrusted code with repository or GitHub App credentials, or bypass the
  merge policy
- the workspace runtime in `runtime/` and its deployment files in `deploy/`

Out of scope:

- the content of source repositories' ontologies, which their own owners maintain
- findings that need an attacker who already has write access to this repository

## Supported versions

Only the current `main` branch is supported. Fixes are not backported.
