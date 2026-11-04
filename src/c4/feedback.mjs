const esc=x=>String(x??'').replace(/[\r\n]+/g,' ').replaceAll('<','&lt;').replaceAll('>','&gt;');
export function renderFeedback(record,assessment) {
 if(record.schemaVersion!==1 || record.assessmentDigest!==assessment.digest || !/^[a-f0-9]{64}$/.test(record.assessmentDigest)) throw new Error('feedback assessment digest mismatch');
 if('approvalStatus' in record || 'approved' in record) throw new Error('approval is not supported');
 const scalar=['architect','reviewer','perceivedAccuracy','perceivedCompleteness','confidence'];
 const lists=['usefulDiscoveries','suspectedMisses','falseLeads','examples','openQuestions'];
 if(scalar.some(k=>typeof record[k]!=='string'||!record[k].trim()) || lists.some(k=>!Array.isArray(record[k])||record[k].some(x=>typeof x!=='string'))) throw new Error('invalid qualitative feedback');
 const out=[`# Pilot feedback: ${esc(assessment.requestId)}`,'',`Assessment digest: ${record.assessmentDigest}`,''];
 for(const k of scalar) out.push(`- ${esc(k.replace(/([A-Z])/g,' $1'))}: ${esc(record[k])}`);
 for(const k of lists) {out.push('',`## ${esc(k.replace(/([A-Z])/g,' $1'))}`); for(const value of record[k]) out.push(`- ${esc(value)}`);}
 return out.join('\n')+'\n';
}
