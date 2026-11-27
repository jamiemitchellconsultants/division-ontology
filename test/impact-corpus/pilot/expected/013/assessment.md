# Impact assessment: CR-1

Change Alpha API 

Assessor: Architect A
Snapshot: e63348017b778e678b0c349160b8ec2bbc659ff5
Relationship master list: none
C4 registry: e63348017b778e678b0c349160b8ec2bbc659ff5
Assessment digest: 550e5c17edea22811e7de98c15c0f5162872f5b974d2c34723d07bb084b8df04

## Confirmed leads
- fixture/Alpha: https://alpha.example/#system — Alpha API

## Not expected to be affected
- fixture/Alpha: https://alpha.example/#external — Out &#124; &lt;b&gt;scope&lt;/b&gt; next

## Likely impacts
- fixture/Alpha: https://alpha.example/#component — Call path; paths a5234bba7631801d0ec30c33dc4966f0596e93193c340851eccb033dea5f8c28; evidence https://alpha.example/#evidence; views 

## Evidence
- https://alpha.example/#evidence — implementation in fixture/Alpha src/app.ts:1-1 at 980b69eca326c7bc54d4c8e34aa067b6fd509409

## Unresolved gaps and questions
- partial-code: No graph code coverage

## Snapshot coverage gaps

## Gaps from cited path traces
- no-relationship-master-list: Impact direction is assumed from subject to object; no relationship master list is defined

## Evidence gaps
