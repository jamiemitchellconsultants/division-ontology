# C4 context view

Catalog commit: `6dbb53695ef3a724da249a566916933cf1bc3559`
Registry commit: `6dbb53695ef3a724da249a566916933cf1bc3559`
View digest: 37a40a762a4d2aa6e2b751d577ae2b88761a1c8a3f8dabca81df4a74091b242f

```mermaid
flowchart LR
  subgraph boundary["https://example.test/ecommerce/graph#system boundary"]
    n5b4c533910742e27["Ecommerce"]
  end
  n82eef6b3764b21e7["Warehouse fulfilment"]
  na926f9020287a4f4["Shopper"]
  n5b4c533910742e27 --> n82eef6b3764b21e7
  na926f9020287a4f4 --> n5b4c533910742e27
  click n82eef6b3764b21e7 "#evidence-82eef6b3764b21e7"
  click na926f9020287a4f4 "#evidence-a926f9020287a4f4"
  click n5b4c533910742e27 "#evidence-5b4c533910742e27"
```

Legend: dashed relationships are inferred and tentative; architect-inspected code is separately identified.

## Evidence

| Item | Assertion / basis | Repository | Commit | Source | Blob |
| --- | --- | --- | --- | --- | --- |
| <a id="evidence-82eef6b3764b21e7"></a>Warehouse fulfilment | https://example.test/ecommerce/graph#connects | fixture/ecommerce | 54666d869c335bd01817d5780feed7ea80bb82be | [docs/architecture.md:1-5](https://github.com/fixture/ecommerce/blob/54666d869c335bd01817d5780feed7ea80bb82be/docs/architecture.md#L1-L5) | 6365581ca2babd6f039e1c5b476f0ee8e24a20a0 |
| <a id="evidence-a926f9020287a4f4"></a>Shopper | https://example.test/ecommerce/graph#uses | fixture/ecommerce | 54666d869c335bd01817d5780feed7ea80bb82be | [docs/acceptance-architecture.md:1-5](https://github.com/fixture/ecommerce/blob/54666d869c335bd01817d5780feed7ea80bb82be/docs/acceptance-architecture.md#L1-L5) | 21c826cfa5f5e9d5d3837e4f42e50f491914cb56 |
| <a id="evidence-5b4c533910742e27"></a>Ecommerce | https://example.test/ecommerce/graph#contains-system | fixture/ecommerce | 54666d869c335bd01817d5780feed7ea80bb82be | [docs/architecture.md:1-5](https://github.com/fixture/ecommerce/blob/54666d869c335bd01817d5780feed7ea80bb82be/docs/architecture.md#L1-L5) | 6365581ca2babd6f039e1c5b476f0ee8e24a20a0 |
| https://ontology-kit.dev/schema#communicates-with | https://example.test/ecommerce/graph#connects / direct | fixture/ecommerce | 54666d869c335bd01817d5780feed7ea80bb82be | [docs/architecture.md:1-5](https://github.com/fixture/ecommerce/blob/54666d869c335bd01817d5780feed7ea80bb82be/docs/architecture.md#L1-L5) | 6365581ca2babd6f039e1c5b476f0ee8e24a20a0 |
| https://ontology-kit.dev/schema#communicates-with | https://example.test/ecommerce/graph#uses / direct | fixture/ecommerce | 54666d869c335bd01817d5780feed7ea80bb82be | [docs/acceptance-architecture.md:1-5](https://github.com/fixture/ecommerce/blob/54666d869c335bd01817d5780feed7ea80bb82be/docs/acceptance-architecture.md#L1-L5) | 21c826cfa5f5e9d5d3837e4f42e50f491914cb56 |

## Coverage

| Source | Scope | Status | Reason |
| --- | --- | --- | --- |
| fixture/accounting | src/** | unknown | No application code is included in this synthetic fixture. |
| fixture/accounting | docs/** | covered |  |
| fixture/accounting | private/** | excluded | Private integration details are deliberately omitted. |
| fixture/ecommerce | src/** | unknown | No application code is included in this synthetic fixture. |
| fixture/ecommerce | docs/** | covered |  |
| fixture/ecommerce | private/** | excluded | Private integration details are deliberately omitted. |
| fixture/warehouse | src/** | unknown | No application code is included in this synthetic fixture. |
| fixture/warehouse | docs/** | covered |  |
| fixture/warehouse | private/** | excluded | Private integration details are deliberately omitted. |

## Gaps

| Code | Source | Scope | Reason |
| --- | --- | --- | --- |
| stale-mapping | fixture/ecommerce |  | Mapping support is unavailable |
| coverage-excluded | fixture/accounting | private/** | Private integration details are deliberately omitted. |
| coverage-excluded | fixture/ecommerce | private/** | Private integration details are deliberately omitted. |
| coverage-excluded | fixture/warehouse | private/** | Private integration details are deliberately omitted. |
| coverage-unknown | fixture/accounting | src/** | No application code is included in this synthetic fixture. |
| coverage-unknown | fixture/ecommerce | src/** | No application code is included in this synthetic fixture. |
| coverage-unknown | fixture/warehouse | src/** | No application code is included in this synthetic fixture. |
