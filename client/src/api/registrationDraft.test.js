import test from 'node:test';
import assert from 'node:assert/strict';
import { clearRegistrationDraft, readRegistrationDraft, saveRegistrationDraft } from './registrationDraft.js';

test('temporary draft excludes files and secrets and expires after three minutes', () => {
  const data = new Map();
  globalThis.sessionStorage = { setItem: (key, value) => data.set(key, value), getItem: (key) => data.get(key) || null, removeItem: (key) => data.delete(key) };
  const realNow = Date.now;
  Date.now = () => 1000;
  try {
    saveRegistrationDraft({ name: 'Test', phoneNumber: '9876543210', password: 'SECRET', otp: '123456' },
      { district: 'Chennai', permanentAddress: 'Synthetic street', aadhaarPhoto: { bytes: 'SECRET' }, aadhaarNumber: '123456789012' });
    const stored = [...data.values()][0];
    assert.ok(!stored.includes('SECRET') && !stored.includes('123456789012'));
    assert.equal(readRegistrationDraft().fields.district, 'Chennai');
    Date.now = () => 181001;
    assert.equal(readRegistrationDraft(), null);
    assert.equal(data.size, 0);
    clearRegistrationDraft();
  } finally { Date.now = realNow; delete globalThis.sessionStorage; }
});
