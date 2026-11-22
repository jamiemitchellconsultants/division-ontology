import cytoscape from './vendor/cytoscape/3.35.0/cytoscape.esm.min.mjs';

const diagrams = new WeakMap();
const ringSlots = 8;
const ringSpacing = 180;

export function diagramFor(host) {
  return diagrams.get(host) ?? null;
}

export function placeNeighbours(ids, previous) {
  const positions = { ...previous };
  let nextSlot = Object.keys(positions).length;
  let ring = 1;
  let firstSlot = 0;
  for (const id of ids) {
    if (Object.hasOwn(positions, id)) continue;
    while (nextSlot >= firstSlot + ringSlots * ring) {
      firstSlot += ringSlots * ring;
      ring++;
    }
    const angle = (nextSlot - firstSlot) * 2 * Math.PI / (ringSlots * ring) - Math.PI / 2;
    positions[id] = { x: ringSpacing * ring * Math.cos(angle), y: ringSpacing * ring * Math.sin(angle) };
    nextSlot++;
  }
  return positions;
}

export function bindGraphActions(cy, callbacks) {
  cy.on('tap', 'node, edge', event => callbacks.inspect(event.target.id()));
  cy.on('dbltap', 'node', event => {
    if (!event.target.hasClass('focus')) callbacks.follow(event.target.id());
  });
}

export function mount(host, dotNetCallbacks) {
  const styles = getComputedStyle(host);
  const ink = styles.getPropertyValue('--graph-ink').trim();
  const paper = styles.getPropertyValue('--graph-paper').trim();
  const cy = cytoscape({
    container: host,
    layout: { name: 'preset', fit: false },
    autounselectify: true,
    autoungrabify: true,
    minZoom: 0.08,
    maxZoom: 3,
    style: [
      { selector: 'node', style: {
        'background-color': paper, 'border-color': ink, 'border-width': 2, color: ink,
        label: 'data(label)', shape: 'round-rectangle', width: 120, height: 50,
        'text-valign': 'center', 'text-wrap': 'wrap', 'text-max-width': '108px',
        'font-family': 'system-ui', 'font-size': 11
      } },
      { selector: 'node.focus', style: {
        'background-color': ink, color: paper, 'font-weight': 'bold'
      } },
      { selector: 'edge', style: {
        width: 2, 'line-color': ink, 'target-arrow-color': ink, 'target-arrow-shape': 'triangle',
        'curve-style': 'bezier', 'control-point-step-size': 65, 'arrow-scale': 1.2,
        label: 'data(label)', color: ink, 'font-family': 'system-ui', 'font-size': 10,
        'text-background-color': paper, 'text-background-opacity': 1, 'text-background-padding': '3px'
      } },
      { selector: 'edge.workspace', style: {
        'line-style': 'dashed'
      } },
      { selector: 'node.inspected', style: { 'border-color': '#2676de', 'border-width': 4 } },
      { selector: 'edge.inspected', style: {
        'line-color': '#2676de', 'target-arrow-color': '#2676de', width: 4
      } }
    ]
  });
  let focus;
  let positions = {};
  bindGraphActions(cy, {
    inspect: id => dotNetCallbacks.invokeMethodAsync('InspectGraphItem', id),
    follow: id => dotNetCallbacks.invokeMethodAsync('FollowGraphNode', id)
  });

  const diagram = {
    render(model) {
      const newFocus = model.focus !== focus;
      if (newFocus) {
        focus = model.focus;
        positions = {};
      }
      positions = placeNeighbours(model.nodes.filter(node => node.id !== focus).map(node => node.id), positions);
      const identities = new Set([...model.nodes, ...model.edges].map(element => element.id));

      cy.batch(() => {
        cy.elements().filter(element => !identities.has(element.id())).remove();
        for (const node of model.nodes) {
          const position = node.id === focus ? { x: 0, y: 0 } : positions[node.id];
          const existing = cy.getElementById(node.id);
          if (existing.empty()) cy.add({ group: 'nodes', data: node, position });
          else existing.data(node).position(position);
          cy.getElementById(node.id).toggleClass('focus', node.id === focus);
        }
        for (const edge of model.edges) {
          const existing = cy.getElementById(edge.id);
          if (existing.empty()) cy.add({ group: 'edges', data: edge });
          else existing.data(edge);
          cy.getElementById(edge.id).toggleClass('workspace', edge.origin === 'runtime');
        }
        cy.elements('.inspected').removeClass('inspected');
        if (model.selectedId) cy.getElementById(model.selectedId).addClass('inspected');
      });
      if (newFocus) cy.fit(cy.elements(), 34);
    },

    captureViewport() {
      return { pan: cy.pan(), zoom: cy.zoom(), positions: { ...positions } };
    },

    restoreViewport(viewport) {
      positions = { ...viewport.positions };
      cy.batch(() => {
        for (const [id, position] of Object.entries(positions)) {
          cy.getElementById(id).position(position);
        }
      });
      cy.viewport({ pan: viewport.pan, zoom: viewport.zoom });
    },

    zoomBy(factor) {
      cy.zoom({
        level: cy.zoom() * factor,
        renderedPosition: { x: cy.width() / 2, y: cy.height() / 2 }
      });
    },

    resetZoom() {
      cy.fit(cy.elements(), 34);
    },

    renderedPosition(id) {
      const element = cy.getElementById(id);
      if (element.empty()) return null;
      return element.isNode() ? element.renderedPosition() : element.renderedMidpoint();
    },

    dispose() {
      diagrams.delete(host);
      cy.destroy();
    }
  };
  diagrams.set(host, diagram);
  return diagram;
}
