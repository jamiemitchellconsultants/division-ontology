# Impact assessment: CR-9

Raise the expense approval threshold 

Assessor: Analyst A
Snapshot: 13c91eac591164b5c6cb8b25c5cf940df293d47e
Source fixture/operations: 0c986d6944c5abdbf0c45252e280731f2c2f19cd
Source fixture/policies: f3477a0bf5dee574afb7248a6ac3bbe9e31f6326
Relationship master list: none
C4 registry: 13c91eac591164b5c6cb8b25c5cf940df293d47e (mappings sha256 348327c19705e40b1fdb3036c5e5fc033cd278133cc7bbfbdac4961df2f5c2c9, bindings sha256 8b9f67aab692f50b53b67407fc6197303537b8018395e13071524d599d7c8724)
Assessment digest: 630bfc3ca6ace101b2cedddeb339d107b5eaa69ec70aa4294fcb31c829ce2879

## Confirmed leads
- fixture/policies: https://example.test/policies/graph#expense-policy — Expense policy

## Not expected to be affected

## Likely impacts
- fixture/policies: https://example.test/policies/graph#ledger — Ledger supports approvals; paths f222e8b2b06da2d177964d79fe6b2873ea2717b56863c7f7c09676e111995e15; evidence https://example.test/policies/graph#ev-policy, https://example.test/policies/graph#ev-approval; views 

## Cited paths
- f222e8b2b06da2d177964d79fe6b2873ea2717b56863c7f7c09676e111995e15
  1. fixture/policies: https://example.test/policies/graph#expense-policy → fixture/policies: https://example.test/policies/graph#approval via https://example.test/rel#governs (outgoing); assertion https://example.test/policies/graph#assert-expense at f3477a0bf5dee574afb7248a6ac3bbe9e31f6326; evidence https://example.test/policies/graph#ev-policy
  2. fixture/policies: https://example.test/policies/graph#approval → fixture/policies: https://example.test/policies/graph#ledger via https://example.test/rel#uses (outgoing); assertion https://example.test/policies/graph#assert-approval at f3477a0bf5dee574afb7248a6ac3bbe9e31f6326; evidence https://example.test/policies/graph#ev-approval

## Evidence
- https://example.test/policies/graph#ev-approval — selected-document in fixture/policies pages/approval-process.md:5-5 at f3477a0bf5dee574afb7248a6ac3bbe9e31f6326
  Page: Approval process (no Confluence address)
      Managers approve claims in the finance system.
- https://example.test/policies/graph#ev-policy — selected-document in fixture/policies pages/expense-policy.md:6-7 at f3477a0bf5dee574afb7248a6ac3bbe9e31f6326
  Page: Expense policy (https://confluence.example.test/pages/1001)
      Claims over 500 need a manager's approval.
      The approval process records every claim.

## Unresolved gaps and questions
- threshold-owner: Who owns the threshold?

## Snapshot coverage gaps

## Gaps from cited path traces
- no-relationship-master-list: Impact direction is assumed from subject to object; no relationship master list is defined
- page-metadata-missing: Exported page has no Confluence address

## Evidence gaps
- page-metadata-missing: fixture/policies pages/approval-process.md — Exported page has no Confluence address
