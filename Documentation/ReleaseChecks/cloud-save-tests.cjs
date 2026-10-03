// Run with node Documentation/ReleaseChecks/cloud-save-tests.cjs (no Unity build).
const assert = require('node:assert/strict');
const fs = require('node:fs');
const vm = require('node:vm');
const source = fs.readFileSync('Assets/plugins/PluginYourGames/Platforms/YandexGames/Plugins/YandexCloudSave.jslib', 'utf8');
const settle = async () => { for (let i = 0; i < 20; i++) await Promise.resolve(); };
function harness({ storage = new Map(), remote = '', identity = 'guest', readFails = false, writeFails = false, hold = false } = {}) {
  let now = 100000, sequence = 0, calls = 0, release;
  const timers = new Map(), messages = [], writes = [];
  const player = {
    getUniqueID: () => identity, getName: () => 'Guest',
    getData: () => readFails ? Promise.reject(Error('offline')) : Promise.resolve({ profile: remote }),
    setData: data => {
      writes.push(data.profile);
      if (writeFails) return Promise.reject(Error('offline'));
      if (hold) return new Promise(resolve => { release = () => { remote = data.profile; resolve(); }; });
      remote = data.profile; return Promise.resolve();
    }
  };
  const context = {
    LibraryManager: { library: {} }, mergeInto: Object.assign, Promise,
    Date: { now: () => now },
    setTimeout: (fn, delay) => { const id = ++sequence; timers.set(id, { fn, at: now + delay }); return id; },
    clearTimeout: id => timers.delete(id),
    localStorage: { getItem: k => storage.get(k) ?? null, setItem: (k, v) => storage.set(k, v) },
    ysdk: { getPlayer: () => { calls++; return Promise.resolve(player); } },
    SendMessage: (obj, method, value) => messages.push({ method, value }), UTF8ToString: v => v
  };
  vm.createContext(context); vm.runInContext(source, context); context.CMCloud = context.LibraryManager.library.$CMCloud;
  return {
    cloud: context.CMCloud, storage, messages, writes, context,
    calls: () => calls, release: () => release(),
    replies: () => messages.filter(m => m.method === 'Reply').map(m => JSON.parse(m.value)),
    load: async () => { context.CMCloud.load('profile', 'Receiver', 'Reply', 1); await settle(); },
    tick: async ms => { now += ms; for (const [id, timer] of [...timers]) if (timer.at <= now) { timers.delete(id); timer.fn(); } await settle(); }
  };
}
(async () => {
  let h = harness({ readFails: true }); await h.load();
  assert.equal(h.replies()[0].ok, false); assert.equal(h.writes.length, 0);
  assert.equal(Object.keys(h.cloud.entries).length, 0, 'failed read must not unlock saving');
  h = harness({ remote: '{broken' }); await h.load(); assert.equal(h.replies()[0].ok, false);
  h = harness({ hold: true }); await h.load();
  h.cloud.save('profile', '{"money":1}');
  assert.equal(JSON.parse(h.storage.get('cm-save-v2:guest:profile')).dirty, true, 'persist before network ack');
  await h.tick(0); h.cloud.save('profile', '{"money":2}'); await h.tick(5000);
  assert.equal(h.writes.length, 1, 'no overlapping writes');
  h.release(); await settle(); await h.tick(0); assert.equal(h.writes[1], '{"money":2}');
  h.release(); await settle(); assert.equal(JSON.parse(h.storage.get('cm-save-v2:guest:profile')).dirty, false);
  for (let i = 0; i < 30; i++) await h.cloud.player();
  assert.equal(h.calls(), 1, 'cache getPlayer');
  h = harness({ writeFails: true }); await h.load(); h.cloud.save('profile', '{"money":3}'); await h.tick(0);
  await h.tick(14000); assert.equal(h.writes.length, 1); await h.tick(1000); assert.equal(h.writes.length, 2);
  const pending = new Map(h.storage);
  h = harness({ storage: new Map(pending) }); await h.load(); assert.equal(h.replies()[0].value, '{"money":3}');
  await h.tick(0); assert.equal(h.writes.length, 1, 'recover pending save on reload');
  for (const useLocal of [false, true]) {
    h = harness({ storage: new Map(pending), remote: '{"money":9}' }); await h.load();
    assert.equal(h.replies()[0].error, 'conflict'); assert.equal(h.writes.length, 0);
    h.cloud.resolve('profile', useLocal); await h.tick(0);
    assert.equal(h.replies()[1].value, useLocal ? '{"money":3}' : '{"money":9}');
    assert.equal(h.writes.length, useLocal ? 1 : 0);
    assert.ok(h.storage.has('cm-save-v2:guest:profile:conflict-backup'));
  }
  h = harness({ storage: new Map(pending), identity: 'other-account' }); await h.load();
  assert.equal(h.replies()[0].value, '', 'do not restore another account');
  console.log('PASS: read failures, malformed data, durable queue, serial writes, cached player, retry, reload recovery, both conflict choices, account isolation.');
})().catch(error => { console.error(error); process.exitCode = 1; });
