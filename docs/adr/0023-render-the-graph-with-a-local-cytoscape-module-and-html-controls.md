# 0023 — Render the graph with a local Cytoscape module and HTML controls

- **Status** accepted
- **Date** 2026-11-21
- **Feature** [#145](https://github.com/jamiemitchellconsultants/division-ontology/issues/145)

## Context

The diagram needs pan, zoom, directed arrows, and separately selectable parallel edges. Append and Back must preserve the reader’s place. Keyboard users need a native route through the same loaded connections and actions. The workspace deploys a static Blazor site to Caddy and AWS static hosting. [Behaviours B1–B8](../features/knowledge-graph-explorer/02-behaviours.md)

## Decision

Use Cytoscape.js behind a small scoped Blazor JavaScript interop module. Keep one diagram per explorer instance. Pass stable node and edge IDs and display fields to the renderer, and return selected IDs to Blazor for the corresponding action. Use preset positions, an expanding ring of stable neighbour slots, and curved edges for parallel relationships. Apply incremental updates while preserving the viewport. Dispose the renderer and callbacks with the component. [Cytoscape.js documentation](https://js.cytoscape.org/) · [Blazor JavaScript interop](https://learn.microsoft.com/en-us/aspnet/core/blazor/javascript-interoperability/call-javascript-from-dotnet?view=aspnetcore-10.0)

Render the accessible neighbour list and controls with native HTML from the same state. Use one entry per relationship and text for origin, source, and direction. The browser module handles drawing and gestures. Blazor owns API calls, selection, navigation, and permission checks. [W3C keyboard guidance](https://www.w3.org/WAI/WCAG22/Understanding/keyboard)

Commit the unmodified browser ESM build (`cytoscape.esm.min.mjs`) from an exact Cytoscape release under `wwwroot` with its MIT license and a recorded upstream version and checksum. Serve it from the same origin with the normal Blazor static assets, and load it on entry to the explorer. This keeps Mac, home-lab, and AWS packaging on their existing .NET publish paths. [Cytoscape.js distribution files](https://js.cytoscape.org/#getting-started/including-cytoscape.js)

## Consequences

The design has a narrow JavaScript integration and maintains a stable graph layout as results arrive. A renderer failure can be reported while the HTML controls remain available. Browser acceptance needs to exercise pointer selection and verify keyboard navigation independently. Updating Cytoscape requires replacing the pinned asset and rerunning diagram and static packaging checks.

## Alternatives considered

- **Build the renderer with Razor and SVG.** This would give full control of the markup and require custom positioning, zoom, hit testing, and parallel edge routing.
- **Load Cytoscape from a public CDN.** A CDN would introduce an external runtime dependency for the private workspace. A bundled asset works with the existing static deployment and versioning.
- **Use the diagram as the sole interaction surface.** Native controls provide reliable keyboard and assistive technology semantics for the same records, including parallel edges.
