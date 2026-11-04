import test from 'node:test';
import assert from 'node:assert/strict';
import {makeModel} from './fixtures/c4/fixture-repos.mjs';
import {projectView} from '../src/c4/project.mjs';
import {renderView} from '../src/c4/render.mjs';
test('same pinned view has byte-identical output and evidence table', () => {
 const {snapshot,registry,keys}=makeModel(),view=projectView({snapshot,registry,focus:keys.alphaSystem,level:'context'}),a=renderView(view),b=renderView(view);
 assert.deepEqual(a,b);assert.equal(JSON.parse(a.json).level,'context');assert.match(a.markdown,/mermaid/);assert.match(a.markdown,/Evidence/);assert.match(a.markdown,/Gaps/);assert.match(a.markdown,new RegExp(snapshot.id.catalogCommit));
});
test('rendered source links are pinned and inference is labelled',()=>{
 const {snapshot,registry,keys}=makeModel(),v=projectView({snapshot,registry,focus:keys.alphaSystem,level:'context'}),{markdown}=renderView(v);
 assert.match(markdown,/github\.com\/fixture\/Alpha\/blob\/c{40}\/src\/app\.ts#L1/);assert.match(markdown,/inferred/i);assert.match(markdown,/#evidence-/);
});
test('special labels do not break Mermaid syntax',()=>{
 const {snapshot,registry,keys}=makeModel(); snapshot.sources.get('fixture/Alpha').graphState.nodes.get(keys.alphaSystem.nodeIri).label='A "name" | <tag>\nnext';
 const {markdown}=renderView(projectView({snapshot,registry,focus:keys.alphaSystem,level:'context'}));
 assert.doesNotMatch(markdown,/\["A "name"/); assert.match(markdown,/&quot;/);
});
test('renders the selected boundary separately from external items',()=>{
 const {snapshot,registry,keys}=makeModel(),v=projectView({snapshot,registry,focus:keys.alphaSystem,level:'context'}),{markdown}=renderView(v);
 assert.match(markdown,/subgraph .*boundary/i); assert.match(markdown,/\n  end\n/);
});
test('report exposes its stable view digest for assessment citations',async()=>{
 const {viewDigest}=await import('../src/c4/assessment.mjs');const {snapshot,registry,keys}=makeModel(),view=projectView({snapshot,registry,focus:keys.alphaSystem,level:'context'});
 assert.ok(renderView(view).markdown.includes(viewDigest(view)));
});
