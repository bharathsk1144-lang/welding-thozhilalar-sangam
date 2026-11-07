import test from 'node:test';
import assert from 'node:assert/strict';
import { readdirSync, readFileSync } from 'node:fs';
import path from 'node:path';

// Run after normal, explicit-demo and separate default-preview builds. Check the
// emitted code, not just source flags, so a bundling/configuration leak fails QA.
function bundle(directory) {
  const assets = path.resolve(directory, 'assets');
  return readdirSync(assets).filter(name => name.endsWith('.js')).map(name => readFileSync(path.join(assets, name), 'utf8')).join('\n');
}
for (const [name, directory, demo] of [
  ['production', 'client/dist', false],
  ['default preview', 'artifacts/readiness-default-preview', false],
  ['explicit demo', 'client/dist-client-preview', true]
]) {
  test(`${name} artifact keeps demo behavior and backend authority isolated`, () => {
    const js = bundle(directory);
    assert.equal(js.includes('No actual registration will be saved'), demo);
    assert.equal(js.includes('Demo registration complete'), demo);
    assert.equal(/["']123456["']/.test(js), false, 'no fixed demo OTP is present in any build');
    if (demo) assert.equal(js.includes('Send demo OTP'), false, 'demo registration does not ask for OTP');
    if (name !== 'production') assert.doesNotMatch(js, /localhost:5087|127\.0\.0\.1:5087|:27017|\/api\/|\/uploads\/|verificationToken|adminToken|PasswordHash|AadhaarPhotoPath/);
  });
}
