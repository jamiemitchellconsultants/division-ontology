import {createHash} from 'node:crypto';
import {readAtCommit,verifyCheckout} from './git.mjs';
import {loadSourceGraph} from './graph.mjs';
// Registered sources must match sources.json exactly; granted sources have no manifest entry (ADR 0012),
// so their clone URL is derived from the repository id.
export function sourceInventory(catalog, manifest) {
  const configMap=new Map(manifest.sources.map(s=>[s.id,s]));
  const registered=catalog.sources.filter(s=>(s.origin??'registered')!=='granted'), granted=catalog.sources.filter(s=>s.origin==='granted');
  if (configMap.size!==manifest.sources.length || new Set(catalog.sources.map(s=>s.id)).size!==catalog.sources.length || registered.length!==configMap.size || registered.some(s=>!configMap.has(s.id)) || granted.some(s=>configMap.has(s.id))) throw new Error('catalog and source inventory mismatch');
  for (const s of granted) configMap.set(s.id,{id:s.id,url:`https://github.com/${s.id}`});
  return configMap;
}
export function loadSnapshot({divisionRoot,catalogCommit,checkouts}) {
  const raw=readAtCommit(divisionRoot,catalogCommit,'dist/catalog.json');
  const manifestRaw=readAtCommit(divisionRoot,catalogCommit,'sources.json');
  if (!raw || !manifestRaw) throw new Error('missing published catalog or sources');
  const catalog=JSON.parse(raw), manifest=JSON.parse(manifestRaw);
  if (!Array.isArray(catalog.sources) || !Array.isArray(manifest.sources)) throw new Error('invalid source inventory');
  const configMap=sourceInventory(catalog,manifest);
  const entries=[...catalog.sources].sort((a,b)=>a.id < b.id ? -1 : a.id > b.id ? 1 : 0);
  const sources=new Map();
  for (const source of entries) {
    const configEntry=configMap.get(source.id), checkout=checkouts?.get(source.id);
    verifyCheckout(checkout,configEntry.url,source.commit);
    const configRaw=readAtCommit(checkout,source.commit,'ontology.config.json');
    const config=configRaw ? JSON.parse(configRaw) : {};
    sources.set(source.id,{id:source.id,commit:source.commit,checkout,config,graphState:loadSourceGraph({checkout,commit:source.commit,config})});
  }
  return {id:{catalogCommit,catalogSha256:createHash('sha256').update(raw).digest('hex'),sources:entries.map(({id,commit})=>({id,commit}))},catalog,sources};
}
