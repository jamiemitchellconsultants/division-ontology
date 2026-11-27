import {createHash} from 'node:crypto';
import {readAtCommit} from './git.mjs';
import {matches} from './graph.mjs';
export function verifyCodeSupplement({snapshot,focus,supplement:s}) {
 if(!s || s.sourceId!==focus.sourceId || s.component?.sourceId!==focus.sourceId || s.component?.nodeIri!==focus.nodeIri) throw new Error('component focus mismatch');
 const source=snapshot.sources.get(s.sourceId); if(!source) throw new Error('source unavailable');
 if(typeof s.reason!=='string' || !s.reason.trim()) throw new Error('inspection reason required');
 const content=readAtCommit(source.checkout,source.commit,s.path); if(content===null) throw new Error('source path missing');
 const bytes=Buffer.from(content), blob=createHash('sha1').update(`blob ${bytes.length}\0`).update(bytes).digest('hex');
 if(blob!==s.blobId) throw new Error('blob mismatch');
 const lines=content.endsWith('\n')?content.split('\n').length-1:content.split('\n').length;
 if(!Number.isSafeInteger(s.startLine)||!Number.isSafeInteger(s.endLine)||s.startLine<1||s.endLine<s.startLine||s.endLine>lines) throw new Error('invalid line range');
 const role=s.role??'implementation', selectors=source.config?.sourceGraph?.evidence?.[role];
 if(!Array.isArray(selectors)||!selectors.some(x=>matches(x,s.path))) throw new Error('role selector mismatch');
 return {...s,role,commit:source.commit,basis:'architect-inspected'};
}
