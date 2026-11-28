# Impact assessment: CR-1

Change Alpha API 

Assessor: Architect A
Snapshot: e63348017b778e678b0c349160b8ec2bbc659ff5
Source fixture/Alpha: 980b69eca326c7bc54d4c8e34aa067b6fd509409
Source fixture/Beta: 9c08221fbb185a173313dd0e3317273cc8d0e992
Relationship master list: none
C4 registry: e63348017b778e678b0c349160b8ec2bbc659ff5 (mappings sha256 17123700fb246c23e5bd212b164935c215763356c9b71d579db6eee160bc926f, bindings sha256 449b49e9cae6b1279721b248bad0cdfe2b59527703525a8548e39e9927b09aab)
Assessment digest: a378148168f4e9700543acf333049b3828bd2ff60cb6e21453a80f64a7158525

## Confirmed leads
- fixture/Alpha: https://alpha.example/#system — Alpha API

## Not expected to be affected
- fixture/Alpha: https://alpha.example/#external — Out &#124; &lt;b&gt;scope&lt;/b&gt; next

## Likely impacts
- fixture/Alpha: https://alpha.example/#component — Call path; paths a5234bba7631801d0ec30c33dc4966f0596e93193c340851eccb033dea5f8c28; evidence https://alpha.example/#evidence; views 

## Cited paths
- a5234bba7631801d0ec30c33dc4966f0596e93193c340851eccb033dea5f8c28
  1. fixture/Alpha: https://alpha.example/#system → fixture/Alpha: https://alpha.example/#container via https://ontology-kit.dev/schema#contains (outgoing); assertion https://alpha.example/#contains1 at 980b69eca326c7bc54d4c8e34aa067b6fd509409; evidence https://alpha.example/#evidence
  2. fixture/Alpha: https://alpha.example/#container → fixture/Alpha: https://alpha.example/#component via https://ontology-kit.dev/schema#contains (outgoing); assertion https://alpha.example/#contains2 at 980b69eca326c7bc54d4c8e34aa067b6fd509409; evidence https://alpha.example/#evidence

## Evidence
- https://alpha.example/#evidence — implementation in fixture/Alpha src/app.ts:1-1 at 980b69eca326c7bc54d4c8e34aa067b6fd509409

## Unresolved gaps and questions
- partial-code: No graph code coverage

## Snapshot coverage gaps

## Gaps from cited path traces
- no-relationship-master-list: Impact direction is assumed from subject to object; no relationship master list is defined

## Evidence gaps
