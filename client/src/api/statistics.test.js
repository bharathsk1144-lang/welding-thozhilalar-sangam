import test from 'node:test';
import assert from 'node:assert/strict';
import { fetchPublicStatistics, notifyStatisticsChanged, observeStatistics, STATISTICS_CHANGED, STATISTICS_STORAGE_KEY } from './statistics.js';

const tick = () => new Promise(resolve => setImmediate(resolve));

test('aggregate fetch distinguishes real zero, omits credentials, and rejects API errors/invalid counts', async () => {
  const signal = new AbortController().signal;
  let options;
  const empty = await fetchPublicStatistics('http://192.168.1.10:5087/api', {
    signal, fetchImpl: async (url, request) => {
      assert.equal(url, 'http://192.168.1.10:5087/api/statistics'); options = request;
      return { ok: true, json: async () => ({ totalRegisteredMembers: 0, configuredDistricts: 0 }) };
    }
  });
  assert.deepEqual(empty, { totalRegisteredMembers: 0, configuredDistricts: 0 });
  assert.equal(options.credentials, 'omit'); assert.equal(options.cache, 'no-store');
  assert.equal(options.signal, signal); assert.deepEqual(options.headers, { Accept: 'application/json' });
  for (const response of [
    { ok: false, status: 503 }, { ok: false, status: 429 },
    ...[{}, null, { totalRegisteredMembers: -1, configuredDistricts: 38 },
      { totalRegisteredMembers: '5', configuredDistricts: 38 },
      { totalRegisteredMembers: 1.5, configuredDistricts: 38 }].map(data => ({ ok: true, json: async () => data }))
  ]) await assert.rejects(fetchPublicStatistics('/api', { fetchImpl: async () => response }), /temporarily unavailable/);
  await assert.rejects(fetchPublicStatistics('/api', { fetchImpl: async () => { throw new Error('offline'); } }));
});

test('mount, registration, focus, page return and cross-tab refresh fetch changed aggregates; dispose stops updates', async () => {
  const target = new EventTarget(), states = [];
  let members = 0, requests = 0;
  const subscription = observeStatistics({ target, onChange: state => states.push(state), load: async () => {
    requests++; return { totalRegisteredMembers: members, configuredDistricts: 38 };
  } });
  await tick(); assert.equal(states.at(-1).data.totalRegisteredMembers, 0);
  for (const eventName of [STATISTICS_CHANGED, 'focus', 'pageshow', 'storage']) {
    members++;
    const event = new Event(eventName);
    if (eventName === 'storage') Object.defineProperty(event, 'key', { value: STATISTICS_STORAGE_KEY });
    target.dispatchEvent(event); await tick();
    assert.equal(states.at(-1).data.totalRegisteredMembers, members);
  }
  const unrelated = new Event('storage'); Object.defineProperty(unrelated, 'key', { value: 'adminToken' });
  target.dispatchEvent(unrelated); await tick(); assert.equal(requests, 5);
  subscription.dispose(); target.dispatchEvent(new Event('focus')); await tick(); assert.equal(requests, 5);
});

test('API errors never turn into zero; manual retry recovers', async () => {
  const states = []; let failed = true;
  const subscription = observeStatistics({ target: new EventTarget(), onChange: state => states.push(state), load: async () => {
    if (failed) throw new Error('503'); return { totalRegisteredMembers: 7, configuredDistricts: 38 };
  } });
  await tick(); assert.deepEqual(states.at(-1), { loading: false, error: true, data: null });
  failed = false; await subscription.refresh(); assert.equal(states.at(-1).data.totalRegisteredMembers, 7);
  subscription.dispose();
});

test('superseded responses cannot overwrite refreshed counts and unmount aborts requests', async () => {
  const requests = [], states = [];
  const subscription = observeStatistics({ target: new EventTarget(), onChange: state => states.push(state),
    load: signal => new Promise(resolve => requests.push({ signal, resolve })) });
  const refreshed = subscription.refresh(); assert.equal(requests[0].signal.aborted, true);
  requests[1].resolve({ totalRegisteredMembers: 2, configuredDistricts: 38 }); await refreshed;
  requests[0].resolve({ totalRegisteredMembers: 1, configuredDistricts: 38 }); await tick();
  assert.equal(states.at(-1).data.totalRegisteredMembers, 2);
  const pending = subscription.refresh(); subscription.dispose();
  assert.equal(requests[2].signal.aborted, true);
  const stateCount = states.length; requests[2].resolve({ totalRegisteredMembers: 99 }); await pending;
  assert.equal(states.length, stateCount);
});

test('timeout becomes recoverable error and registration notification contains no account information', async () => {
  const states = [], target = new EventTarget();
  const subscription = observeStatistics({ target, onChange: state => states.push(state), timeoutMs: 5,
    load: signal => new Promise((_, reject) => signal.addEventListener('abort', () => reject(new Error('timeout')))) });
  await new Promise(resolve => setTimeout(resolve, 20));
  assert.deepEqual(states.at(-1), { loading: false, error: true, data: null }); subscription.dispose();
  let notices = 0, saved;
  target.addEventListener(STATISTICS_CHANGED, event => { notices++; assert.equal(event.detail, undefined); });
  notifyStatisticsChanged(target, { setItem: (key, value) => { saved = { key, value }; } });
  assert.equal(notices, 1); assert.equal(saved.key, STATISTICS_STORAGE_KEY); assert.match(saved.value, /^\d+$/);
  assert.doesNotThrow(() => notifyStatisticsChanged(target, { setItem() { throw new Error('Storage blocked'); } }));
  Object.defineProperty(target, 'localStorage', { get() { throw new Error('Storage access blocked'); } });
  assert.doesNotThrow(() => notifyStatisticsChanged(target));
});
