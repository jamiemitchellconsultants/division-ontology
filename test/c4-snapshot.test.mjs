import test from 'node:test';
import assert from 'node:assert/strict';
import {createHash} from 'node:crypto';
import {withPilotFixture} from './fixtures/c4/fixture-repos.mjs';
import {loadSnapshot} from '../src/c4/snapshot.mjs';
import {readAtCommit} from '../src/c4/git.mjs';
test('snapshot uses published bytes and pinned source commits', async () => {
  await withPilotFixture(async f => {
    const s = loadSnapshot(f);
    const raw = readAtCommit(f.divisionRoot,f.catalogCommit,'dist/catalog.json');
    assert.equal(s.id.catalogSha256,createHash('sha256').update(raw).digest('hex'));
    assert.deepEqual(s.id.sources,[{id:'fixture/Alpha',commit:f.sourceCommits.alpha},{id:'fixture/Beta',commit:f.sourceCommits.beta}]);
    assert.equal(s.sources.get('fixture/Alpha').graphState.status,'unavailable');
  });
});
test('missing and wrong source checkouts fail closed', async () => {
  await withPilotFixture(async f => {
    assert.throws(() => loadSnapshot({...f,checkouts:new Map()}),/checkout/i);
    const wrong = new Map(f.checkouts); wrong.set('fixture/Alpha',f.checkouts.get('fixture/Beta'));
    assert.throws(() => loadSnapshot({...f,checkouts:wrong}),/origin|identity/i);
  });
});
test('dirty source checkout fails closed', async () => {
  await withPilotFixture(async f => { f.writeSource('fixture/Alpha','README.md','modified'); assert.throws(()=>loadSnapshot(f),/dirty/i); });
});
test('unavailable source commit fails closed', async () => {
  await withPilotFixture(async f => { assert.throws(()=>readAtCommit(f.checkouts.get('fixture/Alpha'),'0'.repeat(40),'README.md'),/commit/i); });
});
