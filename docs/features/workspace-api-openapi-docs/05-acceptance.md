# Workspace API OpenAPI docs acceptance

Feature [#105](https://github.com/jamiemitchellconsultants/division-ontology/issues/105).
The [behaviours and runbook](feature.md) define the contract, environment setup and recovery.

## Scripted walkthrough

Use the reviewed build in the local demo, then repeat the live steps on the home-lab HTTPS origin
with Keycloak and on the AWS CloudFront origin with Entra. Bring a reader, a manager, an account
without workspace roles, and a published entity reserved for acceptance. Use a disposable local
demo or an approved test entity on a deployment. The manager's test creates an attributed change
in real history. Keep credentials and bearer tokens out of evidence and issue comments.

| Step | Behaviour | Action | Expected result |
| --- | --- | --- | --- |
| 1 | B1 | Open the spec and compare the method and path list with WorkspaceRoutes and the health route. Expand a paged read, a contribution and the health check. | Every mapped operation has a summary, applicable parameters (including `cursor` and `limit`), schemas and the right role. Health is anonymous. |
| 2 | B2 | Inspect error responses on inventory and the edit contribution route. Check the shared 500 response. | Operation errors are problem details with codes. Inventory includes 400, 401 and 403. Edit 409 describes `current`. Pre-selection transport errors are outside the operation response list. |
| 3 | B3 | Run the completeness suite and inspect its deliberately incomplete inputs. Run the full scenario suite to exercise observed statuses. | The real spec is complete. The negative tests name each affected operation and gap, including missing 400 and an undeclared observed status. |
| 4 | B4 | Browse the example request bodies for annotations, relationships, search and traversal. | Examples use invented values and contain no real names, sources or catalog content. |
| 5 | B5 | In a disposable checkout, remove the generated spec from the working tree and run `dotnet build runtime/RuntimeKnowledge.Api`. | The build recreates `runtime/openapi/workspace-api.json`. A contract-changing revision presents its regenerated contract as a diff against its base. |
| 6 | B6 | In that checkout, temporarily remove the generated file and run the drift check's comparison (`git status --porcelain -- runtime/openapi`). Run the build and compare again. Inspect the `api-contract` job on the actual PR. | The first comparison detects drift. CI's failure branch names `dotnet build runtime/RuntimeKnowledge.Api` as the remedy. The rebuilt, matching contract passes. |
| 7 | B7 | Review the build PR's route changes and generated spec together. | The PR carries the generated contract beside the corresponding code. |
| 8 | B8 | Regenerate with no workspace providers configured. Inspect the committed JSON and review CI's rebuild with deployment-shaped variables. | Both builds produce identical JSON without deployment values or provider dependencies. |
| 9 | B9 | From a fresh browser profile, open `/api/docs` and choose **Authorize** under `workspaceSignIn`. Select the API scope if shown and sign in to the provider. Repeat in a separate profile for each role. | Swagger sends an authorization-code request with PKCE to the configured provider, asks for no client secret, and returns to the docs authorized. |
| 10 | B10 | As reader, execute `GET /api/session` and `GET /api/inventory?limit=1` using **Try it out**. Compare the read with the same authorized call directly. | The request stays on the docs origin. The inventory call returns 200 and the same data. |
| 11 | B11 | As manager, copy the inventory entity's key into the `target` of a new annotation. Use category `businessExplanation` and distinctive test text. Execute `POST /api/contributions/annotations`, then call `GET /api/history` with the returned `id` as `contributionId`. | Creation returns 201. History returns the change with the same contribution ID, test text and manager actor. |
| 12 | B12 | As reader, submit a second, uniquely marked annotation request. Recheck recent history as manager. In a separate profile sign in as an account with neither workspace role and try inventory. | Both protected requests return 403 `roleRequired`. The reader's attempted change never appears in history. The direct API health probe still succeeds without a token. |
| 13 | B13 | In a controlled test session, let the access token expire and retry a read. Reauthorize in the same page and retry. Then use the docs dialog's **Logout** and retry the read. | Expired and signed-out calls return 401 `authenticationRequired`. Reauthorization restores 200. After logout the browser stops attaching the token. |
| 14 | B14 | Fetch the served JSON and compare it with the generated file after removing `workspaceSignIn` and its operation requirements. Check the request URL in Swagger. | The remaining JSON is the same contract. Neither file has a `servers` entry. Swagger sends requests to the origin serving the docs. |
| 15 | B15 | Run `verify-docs.sh <origin> --expect-sign-in` using its repository path. Follow the redirect from `/api/docs`, load the web UI and check API readiness using the deployment health probe. | The spec, docs, callback and web UI are served at their documented paths, and the API health probe succeeds. The anonymous inventory call returns the expected 401. |
| 16 | B16 | Initialize a disposable fresh division copy with `init-division`, regenerate its spec and deploy it with the local instructions. Run the docs deployment check on its origin. | The initialized copy's spec matches its build and both docs routes work with the inherited deployment settings. |
| 17 | B17 | Inspect the effective SPA redirect registration and compare it with the runbook or Terraform output. | The exact origin plus `/api/docs/oauth2-redirect.html` is registered beside the web callback. For Entra it is on the SPA platform. |
| 18 | B18 | In a fresh profile open the docs and fetch the spec before signing in. | Both load without any identity provider prompt. |
| 19 | B19 | Before signing in, execute an inventory read in Swagger and call the same operation with curl. Check the API health probe separately. | Both inventory calls return 401 `authenticationRequired`. Health remains callable. |
| 20 | B20 | Inspect the served JSON and compare with the public web UI identity settings and the committed document. | The only deployment values are the public sign-in details. There are no credentials, database/storage details or published records. |
| 21 | B21 | Compare the docs and web UI session details for the same reader and manager. Use the browser request details locally to compare the audience claims without logging bearer tokens. | Both clients use the same API audience. Read and manage capabilities match and the reader's contribution is refused. |
| 22 | B22 | As manager, send malformed JSON with `application/json` to the annotation operation. Compare its response with the refusals in steps 12, 13 and 19. | Responses carry their original 400, 401 or 403, a fixed title and respectively `unreadableRequest`, `authenticationRequired` or `roleRequired`. A 401 has `WWW-Authenticate: Bearer` and the same problem fields for missing, invalid or expired tokens, with a per-request trace ID. |

## Executable checks

From the repository root, run the existing checks below. The GitHub job covers the build and
static contract suite. The database-backed suite covers actual request/response behaviour with
fixture identities. A green fixture test is evidence for its assertions. The live browser steps
also exercise the identity provider's real registration and callback.

For a disposable local Keycloak demo, run `node deploy/workspace/verify-docs-browser.mjs` after
publishing the fixture snapshot. Set `WORKSPACE_PORT` to the demo's port and supply
`WORKSPACE_DEV_READER_PASSWORD` and `WORKSPACE_DEV_MANAGER_PASSWORD` in the environment.
Set `PLAYWRIGHT_CHROMIUM_EXECUTABLE` to the path to a compatible installed Chromium browser
if the Playwright-managed executable is unavailable. The test creates an annotation, so run
it against a disposable fixture. The same-origin API assertions cover browser sign-in, reading,
saving, history, role refusals and logout. Use the scripted steps for deployed identities.

```bash
npm test
bash deploy/workspace/local/smoke.sh
bash deploy/workspace/database/test-db.sh run dotnet test runtime/RuntimeKnowledge.Tests
deploy/workspace/home-lab/verify-config.sh
deploy/workspace/aws/verify-terraform.sh
deploy/workspace/verify-docs.sh <origin> --expect-sign-in
```

`verify-terraform.sh` initializes the AWS provider and uses the repository's Terraform tests.
Agree to the provider download before running it. Use the origin check after the build PR
merges and the operator deploys it. The web UI scenarios in the local smoke provide role and
history regression coverage. Run the Swagger-specific steps separately.

| Behaviour | Executable evidence in the repository |
| --- | --- |
| B1 | `EndpointDescriptionTests`, `DocumentDescriptionTests`, `CompletenessTests` |
| B2 | `DocumentDescriptionTests`, `WorkspaceErrorsTests`, `ResponseConformanceTests`, database suite conformance hook |
| B3 | `CompletenessTests`, `ResponseConformanceTests`, CI `api-contract` |
| B4 | `ExampleTests` |
| B5 | CI `api-contract` regeneration and `CommittedSpecTests` |
| B6 | CI `api-contract` drift step and `test/api-contract.test.mjs` |
| B7 | CI `api-contract` drift step on the build PR |
| B8 | `CommittedSpecTests` and CI's deployment-settings rebuild |
| B9 | `DocsEndpointTests`, `DocsSignInTests` and `verify-docs-browser.mjs` |
| B10 | `IdentityTests` and `verify-docs-browser.mjs` |
| B11 | `ContributionTests` and `verify-docs-browser.mjs` |
| B12 | `RefusalResponseTests`, `IdentityTests` and `verify-docs-browser.mjs` |
| B13 | `RefusalResponseTests` tests expired and missing tokens. `verify-docs-browser.mjs` checks logout and same-page reauthorization. |
| B14 | `ServedSpecTests`, `verify-docs.sh` |
| B15 | `DocsEndpointTests`, `AwsSimulationTests`, `verify-docs.sh` on each origin, local smoke |
| B16 | `test/init-division.test.mjs`, locked regeneration in `api-contract`, `verify-docs.sh` on the initialized copy |
| B17 | `test/workspace-docs-config.test.mjs`, `home-lab/verify-config.sh`, Terraform workspace tests |
| B18 | `DocsEndpointTests`, `AwsSimulationTests`, `verify-docs.sh` |
| B19 | `DocsEndpointTests`, `RefusalResponseTests`, `verify-docs.sh` |
| B20 | `ServedSpecTests`, `ExampleTests` |
| B21 | `IdentityTests`, `DocsEndpointTests`, `test/workspace-docs-config.test.mjs`, Terraform workspace tests |
| B22 | `RefusalResponseTests`, `AwsSimulationTests`, `WorkspaceUiTests` |

## Results

Acceptance operator is Codex on 19 November 2026. The local test command used SDK 10.0.302.
The isolated Linux reproduction used SDK 10.0.402, the version in `runtime/global.json`.

| Environment and check | Result | Evidence |
| --- | --- | --- |
| Local database-backed .NET suite | Pass | 136 passed, 0 failed, 0 skipped. Includes Docker-free OpenAPI checks, database-backed response conformance, and Keycloak and Entra token fixtures. |
| Node suite | Pass | `npm test` reported 245 passed, 0 failed, 0 skipped. |
| Home-lab configuration | Pass | `home-lab/verify-config.sh` exited 0. |
| Local Swagger PKCE dialog (B9) | Fail | The reader and manager both completed the Keycloak PKCE round trip with the field blank. Swagger displayed a visible `client_secret` password field before both logins. |
| Local Swagger reader and manager (B10–B13, B18–B19, B21–B22) | Pass for the exercised steps | In Chrome at `http://workspace.localhost:18088/api/docs` the reader's inventory matched a direct 200. The manager created an attributed annotation (201) and found it in history. The reader received 403 `roleRequired`, with no matching history change. Swagger sent no bearer header after logout and returned 401 `authenticationRequired`. Reauthorization restored 200 in the same page. An unreadable manager request returned 400 `unreadableRequest`. |
| Local Compose smoke | Pass | Retried with `PLAYWRIGHT_CHROMIUM_EXECUTABLE` pointing to installed Chrome. The docs check with `--expect-sign-in`, browser and API smoke, persistence and replay all passed. The replay digest matched at journal position 5. |
| Local served spec and readiness (B14, B15, B20) | Pass | After removing only `workspaceSignIn` and its operation requirements, served and committed JSON matched. The web UI returned 200 and the API container's health returned `{"status":"ready"}`. |
| Local contract walkthrough (B1–B8) | Pass for local checks | Reviewed all 13 operations and the four synthetic examples. In a disposable checkout the drift step rejected a missing spec and printed the regeneration command. The normal build recreated the identical file, the drift step passed, and the PR carries the file together with its source changes. |
| Isolated Linux SDK 10.0.402 check | Pass | Locked restore, both API builds (with and without deployment-shaped settings), byte-for-byte spec comparisons and all 98 OpenAPI tests passed in the pinned SDK container. |
| GitHub `api-contract` | Fail | On [the first PR #112 run](https://github.com/jamiemitchellconsultants/division-ontology/actions/runs/44551136678/job/135654855477), locked restore failed with NU1004 in the Web project. The lock file expects `Microsoft.NET.ILLink.Tasks` 10.0.13 and the runner requested 10.0.15. CI skipped spec generation, drift and completeness. |
| Terraform validation and fixtures | Pass | `verify-terraform.sh` installed signed AWS provider 6.66.0 with approval. Validation passed and `private_database_and_serverless_api` passed (1/1). |
| Home lab and AWS | Not run | Live origin and account access were not supplied. After review, merge and deployment, the workspace operator runs `verify-docs.sh <origin> --expect-sign-in` and the live steps on each origin, including home-lab Keycloak and AWS Entra sign-in. Record the date, operator, deployment revision and step outcomes here. |

All deployed walkthroughs and executable checks are **not run**. The home-lab configuration and
Terraform results above validate the repository fixtures.

| Behaviour | Local walkthrough result | Executable result |
| --- | --- | --- |
| B1–B4 | Pass. Reviewed the route list, roles, response shapes and synthetic examples. | Pass. OpenAPI tests and database-backed conformance. |
| B5–B8 | Pass locally. Inspected PR #112 and reproduced neutral builds and drift detection in disposable checkouts. | Pass locally on 10.0.302 and in the isolated 10.0.402 container. GitHub `api-contract` failed before these steps. |
| B9 | Fail. Both accounts completed the OAuth round trip and both saw the secret prompt. | Fail. `verify-docs-browser.mjs` exited 1 for the prompt after continuing the remaining live checks. |
| B10–B11 | Pass. Reader got 200 and manager's 201 was attributed and visible in history. | Pass. The browser steps and the .NET role and contribution tests passed. |
| B12 | Pass for the reader. A live roleless user was not supplied. | Pass. Browser reader attempt and roleless-token .NET fixture passed. |
| B13 | Pass for sign-out and same-page reauthorization. Live expiry was not run. | Pass. Browser logout and reauthorization checks and the .NET expiry fixture passed. |
| B14–B15 | Pass. Compared served JSON and checked the docs redirect, web UI and readiness. | Pass. Local deployment script, OpenAPI tests and full smoke. |
| B16 | Pass. Initialized a new division copy, regenerated the same spec and served its docs from a disposable Compose deployment. | Pass. `init-division.test.mjs`, unchanged spec after build, and `verify-docs.sh http://workspace.localhost:18089 --expect-sign-in`. |
| B17 | Pass locally. Observed the Keycloak callback on both docs logins. | Pass. Node configuration tests, home-lab check, Terraform fixture and actual local sign-ins. |
| B18–B19 | Pass. Opened docs and attempted a read in an anonymous page. | Pass. Browser, deployment script and .NET checks. |
| B20 | Pass. Compared the committed contract with the served document and inspected the sign-in addition. | Pass. Served-spec and example tests. |
| B21 | Pass for the local workspace audience and reader/manager role checks. A side-by-side browser token comparison with the web UI was not run. | Pass. Browser session checks and fixture identity, configuration and Terraform tests. |
| B22 | Pass. Triggered 400, 401 and 403 from the docs UI. | Pass. Browser checks and .NET refusal tests, including missing, invalid and expired 401 cases. |
