# C4 container view

Catalog commit: `7442de3a6a7929f51124be38bff0f7709207a9ef`
Registry commit: `7442de3a6a7929f51124be38bff0f7709207a9ef`
View digest: 992e8fda4d5452598180fefa2f2f518c1f917ec606a2382120463d62dff49384

```mermaid
flowchart LR
  subgraph boundary["https://example.test/ecommerce/graph#system boundary"]
    nb913156cd268c305["Storefront API"]
    n01d3023c94c8126f["Order store"]
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
| <a id="evidence-d3ed265d10f08973"></a>Address validation | https://example.test/ecommerce/graph#has-address | fixture/ecommerce | 54666d869c335bd01817d5780feed7ea80bb82be | [docs/acceptance-architecture.md:1-5](https://github.com/fixture/ecommerce/blob/54666d869c335bd01817d5780feed7ea80bb82be/docs/acceptance-architecture.md#L1-L5) | c18767a9dc1fb28a12fed1390185f31bd740b30b |
| <a id="evidence-b913156cd268c305"></a>Storefront API | https://example.test/ecommerce/graph#contains-system | fixture/ecommerce | 54666d869c335bd01817d5780feed7ea80bb82be | [docs/architecture.md:1-5](https://github.com/fixture/ecommerce/blob/54666d869c335bd01817d5780feed7ea80bb82be/docs/architecture.md#L1-L5) | 6365581ca2babd6f039e1c5b476f0ee8e24a20a0 |
| <a id="evidence-01d3023c94c8126f"></a>Order store | https://example.test/ecommerce/graph#has-store | fixture/ecommerce | 54666d869c335bd01817d5780feed7ea80bb82be | [docs/acceptance-architecture.md:1-5](https://github.com/fixture/ecommerce/blob/54666d869c335bd01817d5780feed7ea80bb82be/docs/acceptance-architecture.md#L1-L5) | c18767a9dc1fb28a12fed1390185f31bd740b30b |
| https://ontology-kit.dev/schema#contains | https://example.test/ecommerce/graph#has-address / direct | fixture/ecommerce | 54666d869c335bd01817d5780feed7ea80bb82be | [docs/acceptance-architecture.md:1-5](https://github.com/fixture/ecommerce/blob/54666d869c335bd01817d5780feed7ea80bb82be/docs/acceptance-architecture.md#L1-L5) | c18767a9dc1fb28a12fed1390185f31bd740b30b |
| https://ontology-kit.dev/schema#communicates-with | https://example.test/ecommerce/graph#reads / direct | fixture/ecommerce | 54666d869c335bd01817d5780feed7ea80bb82be | [docs/acceptance-architecture.md:1-5](https://github.com/fixture/ecommerce/blob/54666d869c335bd01817d5780feed7ea80bb82be/docs/acceptance-architecture.md#L1-L5) | c18767a9dc1fb28a12fed1390185f31bd740b30b |
| https://ontology-kit.dev/schema#communicates-with | https://example.test/ecommerce/graph#writes / direct | fixture/ecommerce | 54666d869c335bd01817d5780feed7ea80bb82be | [docs/acceptance-architecture.md:1-5](https://github.com/fixture/ecommerce/blob/54666d869c335bd01817d5780feed7ea80bb82be/docs/acceptance-architecture.md#L1-L5) | c18767a9dc1fb28a12fed1390185f31bd740b30b |

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
