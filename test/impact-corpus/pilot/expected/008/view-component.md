# C4 component view

Catalog commit: `e63348017b778e678b0c349160b8ec2bbc659ff5`
Registry commit: `e63348017b778e678b0c349160b8ec2bbc659ff5`
View digest: 840b723633454cbb2de62547222907e6d331aa3506c4de098c97ad91269b2750

```mermaid
flowchart LR
  subgraph boundary["https://alpha.example/#container boundary"]
    nb593fe97e1c097aa["API handler"]
  end
  click nb593fe97e1c097aa "#evidence-b593fe97e1c097aa"
```

Legend: dashed relationships are inferred and tentative; architect-inspected code is separately identified.

## Evidence

| Item | Assertion / basis | Repository | Commit | Source | Blob |
| --- | --- | --- | --- | --- | --- |
| <a id="evidence-b593fe97e1c097aa"></a>API handler | https://alpha.example/#contains2 | fixture/Alpha | 980b69eca326c7bc54d4c8e34aa067b6fd509409 | [src/app.ts:1-1](https://github.com/fixture/Alpha/blob/980b69eca326c7bc54d4c8e34aa067b6fd509409/src/app.ts#L1) | 129fbfa125de7a8b2b24c3dbc34ef5d0e0570747 |

## Coverage

| Source | Scope | Status | Reason |
| --- | --- | --- | --- |
| fixture/Alpha | src/** | covered |  |
| fixture/Beta | src/** | covered |  |

## Gaps

| Code | Source | Scope | Reason |
| --- | --- | --- | --- |
| None recorded | | | Coverage does not establish architectural completeness. |
