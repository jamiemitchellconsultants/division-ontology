# C4 container view

Catalog commit: `e63348017b778e678b0c349160b8ec2bbc659ff5`
Registry commit: `e63348017b778e678b0c349160b8ec2bbc659ff5`
View digest: 1524cdc762e33f90f76b71e9dd748b1c0c03f3d56566f8d011a8e2828b067d36

```mermaid
flowchart LR
  subgraph boundary["https://alpha.example/#system boundary"]
    n5073737abe10f07c["Alpha service"]
  end
  click n5073737abe10f07c "#evidence-5073737abe10f07c"
```

Legend: dashed relationships are inferred and tentative; architect-inspected code is separately identified.

## Evidence

| Item | Assertion / basis | Repository | Commit | Source | Blob |
| --- | --- | --- | --- | --- | --- |
| <a id="evidence-5073737abe10f07c"></a>Alpha service | https://alpha.example/#contains1 | fixture/Alpha | 980b69eca326c7bc54d4c8e34aa067b6fd509409 | [src/app.ts:1-1](https://github.com/fixture/Alpha/blob/980b69eca326c7bc54d4c8e34aa067b6fd509409/src/app.ts#L1) | 129fbfa125de7a8b2b24c3dbc34ef5d0e0570747 |

## Coverage

| Source | Scope | Status | Reason |
| --- | --- | --- | --- |
| fixture/Alpha | src/** | covered |  |
| fixture/Beta | src/** | covered |  |

## Gaps

| Code | Source | Scope | Reason |
| --- | --- | --- | --- |
| None recorded | | | Coverage does not establish architectural completeness. |
