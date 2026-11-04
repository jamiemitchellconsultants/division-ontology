import test from 'node:test';
import assert from 'node:assert/strict';
import {renderFeedback} from '../src/c4/feedback.mjs';
test('separate qualitative feedback records misses and uncertainty',()=>{
 const assessment={requestId:'CR-1',digest:'a'.repeat(64)},feedback={schemaVersion:1,assessmentDigest:assessment.digest,architect:'Architect A',reviewer:'Reviewer B',perceivedAccuracy:'mostly accurate',perceivedCompleteness:'partial',confidence:'medium',usefulDiscoveries:['Beta dependency'],suspectedMisses:['data store'],falseLeads:['legacy API'],examples:['CR-1 path'],openQuestions:['Who owns the data store?']};
 const markdown=renderFeedback(feedback,assessment);assert.match(markdown,/suspected misses/i);assert.match(markdown,/data store/);assert.equal('approvalStatus' in feedback,false);
 assert.throws(()=>renderFeedback({...feedback,assessmentDigest:'0'.repeat(64)},assessment),/digest/i);
});
