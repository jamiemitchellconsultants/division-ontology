# C4 container view

Catalog commit: `6dbb53695ef3a724da249a566916933cf1bc3559`
Registry commit: `6dbb53695ef3a724da249a566916933cf1bc3559`
View digest: dbad638800cd87b532d76feea4065887e4516c47773cef48afb16d4a6c17b2af

```mermaid
flowchart LR
  subgraph boundary["https://example.test/ecommerce/graph#system boundary"]
    nb913156cd268c305["Storefront API"]
    n01d3023c94c8126f["Order store v2"]
  end
  nd3ed265d10f08973["Address validation"]
  nb913156cd268c305 --> nd3ed265d10f08973
  nd3ed265d10f08973 --> n01d3023c94c8126f
  nb913156cd268c305 --> n01d3023c94c8126f
  click nd3ed265d10f08973 "#evidence-d3ed265d10f08973"
  click nb913156cd268c305 "#evidence-b913156cd268c305"
  click n01d3023c94c8126f "#evidence-01d3023c94c8126f"
```

Legend: dashed relationships are inferred and tentative; architect-inspected code is separately identified.

## Evidence

| Item | Assertion / basis | Repository | Commit | Source | Blob |
| --- | --- | --- | --- | --- | --- |
| <a id="evidence-d3ed265d10f08973"></a>Address validation | https://example.test/ecommerce/graph#has-address | fixture/ecommerce | 54666d869c335bd01817d5780feed7ea80bb82be | [docs/acceptance-architecture.md:1-5](https://github.com/fixture/ecommerce/blob/54666d869c335bd01817d5780feed7ea80bb82be/docs/acceptance-architecture.md#L1-L5) | 21c826cfa5f5e9d5d3837e4f42e50f491914cb56 |
| <a id="evidence-b913156cd268c305"></a>Storefront API | https://example.test/ecommerce/graph#contains-system | fixture/ecommerce | 54666d869c335bd01817d5780feed7ea80bb82be | [docs/architecture.md:1-5](https://github.com/fixture/ecommerce/blob/54666d869c335bd01817d5780feed7ea80bb82be/docs/architecture.md#L1-L5) | 6365581ca2babd6f039e1c5b476f0ee8e24a20a0 |
| <a id="evidence-01d3023c94c8126f"></a>Order store v2 | https://example.test/ecommerce/graph#has-store | fixture/ecommerce | 54666d869c335bd01817d5780feed7ea80bb82be | [docs/acceptance-architecture.md:1-5](https://github.com/fixture/ecommerce/blob/54666d869c335bd01817d5780feed7ea80bb82be/docs/acceptance-architecture.md#L1-L5) | 21c826cfa5f5e9d5d3837e4f42e50f491914cb56 |
| https://ontology-kit.dev/schema#contains | https://example.test/ecommerce/graph#has-address / direct | fixture/ecommerce | 54666d869c335bd01817d5780feed7ea80bb82be | [docs/acceptance-architecture.md:1-5](https://github.com/fixture/ecommerce/blob/54666d869c335bd01817d5780feed7ea80bb82be/docs/acceptance-architecture.md#L1-L5) | 21c826cfa5f5e9d5d3837e4f42e50f491914cb56 |
| https://ontology-kit.dev/schema#communicates-with | https://example.test/ecommerce/graph#reads / direct | fixture/ecommerce | 54666d869c335bd01817d5780feed7ea80bb82be | [docs/acceptance-architecture.md:1-5](https://github.com/fixture/ecommerce/blob/54666d869c335bd01817d5780feed7ea80bb82be/docs/acceptance-architecture.md#L1-L5) | 21c826cfa5f5e9d5d3837e4f42e50f491914cb56 |
| https://ontology-kit.dev/schema#communicates-with | https://example.test/ecommerce/graph#writes / direct | fixture/ecommerce | 54666d869c335bd01817d5780feed7ea80bb82be | [docs/acceptance-architecture.md:1-5](https://github.com/fixture/ecommerce/blob/54666d869c335bd01817d5780feed7ea80bb82be/docs/acceptance-architecture.md#L1-L5) | 21c826cfa5f5e9d5d3837e4f42e50f491914cb56 |

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
