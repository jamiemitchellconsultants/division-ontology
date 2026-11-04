import {createHash} from 'node:crypto';
const digest=x=>createHash('sha256').update(JSON.stringify(x)).digest('hex').slice(0,16);
const itemId=key=>'n'+digest(key);
const escape=x=>String(x??'').replaceAll('&','&amp;').replaceAll('<','&lt;').replaceAll('>','&gt;').replaceAll('"','&quot;').replaceAll('|','&#124;').replace(/[\r\n]+/g,' ');
const link=(id,commit,e)=>`https://github.com/${id.split('/').map(encodeURIComponent).join('/')}/blob/${commit}/${e.path.split('/').map(encodeURIComponent).join('/')}#L${e.startLine}${e.endLine!==e.startLine?'-L'+e.endLine:''}`;
export function renderView(view) {
 const json=JSON.stringify(view,null,2)+'\n';
 const out=[`# C4 ${escape(view.level)} view`,'',`Catalog commit: \`${escape(view.snapshotId.catalogCommit)}\``, `Registry commit: \`${escape(view.registryCommit)}\``, '', '```mermaid','flowchart LR'];
 out.push(`  subgraph boundary["${escape(view.focus.nodeIri)} boundary"]`);
 for(const item of view.items.filter(x=>x.insideBoundary)) out.push(`    ${itemId(item.key)}["${escape(item.label)}"]`);
 out.push('  end');
 for(const item of view.items.filter(x=>!x.insideBoundary)) out.push(`  ${itemId(item.key)}["${escape(item.label)}"]`);
 for(const edge of view.edges) out.push(`  ${itemId(edge.from)} ${edge.basis==='inferred'?'-.->':'-->'} ${itemId(edge.to)}`);
 for(const item of view.items) out.push(`  click ${itemId(item.key)} "#evidence-${digest(item.key)}"`);
 out.push('```','','Legend: dashed relationships are inferred and tentative; architect-inspected code is separately identified.','','## Evidence','','| Item | Assertion / basis | Repository | Commit | Source | Blob |','| --- | --- | --- | --- | --- | --- |');
 for(const item of view.items) for(const e of item.evidence ?? []) out.push(`| <a id="evidence-${digest(item.key)}"></a>${escape(item.label)} | ${escape(item.assertionIri ?? item.basis)} | ${escape(item.key.sourceId)} | ${escape(item.commit)} | [${escape(e.path)}:${e.startLine}-${e.endLine}](${link(item.key.sourceId,item.commit,e)}) | ${escape(e.blobId)} |`);
 for(const edge of view.edges) for(const e of edge.evidence ?? []) if(e) out.push(`| ${escape(edge.relationship)} | ${escape(edge.assertionIri)} / ${escape(edge.basis)} | ${escape(edge.from.sourceId)} | ${escape(edge.commit)} | [${escape(e.path)}:${e.startLine}-${e.endLine}](${link(edge.from.sourceId,edge.commit,e)}) | ${escape(e.blobId)} |`);
 out.push('','## Gaps','','| Code | Reason |','| --- | --- |');
 for(const gap of view.gaps) out.push(`| ${escape(gap.code)} | ${escape(gap.reason)} |`);
 if(!view.gaps.length) out.push('| None recorded | Coverage does not establish architectural completeness. |');
 return {json,markdown:out.join('\n')+'\n'};
}
