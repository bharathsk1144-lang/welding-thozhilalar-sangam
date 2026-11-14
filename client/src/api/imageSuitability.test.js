import test from 'node:test';
import assert from 'node:assert/strict';
import { inspectImage } from './imageSuitability.js';

const file = (type = 'image/png', bytes = [0x89, 0x50, 0x4e, 0x47]) => ({ type, size: 1000, slice: () => ({ arrayBuffer: async () => Uint8Array.from(bytes).buffer }) });
test('image suitability rejects mismatched signatures, group portraits and unrelated documents', async () => {
  globalThis.window = {};
  globalThis.createImageBitmap = async () => ({ width: 600, height: 750, close() {} });
  try {
    assert.match((await inspectImage(file('image/png', [60, 104, 116, 109]), 'passport')).error, /contents/);
    window.FaceDetector = class { async detect() { return [{ boundingBox: { x: 10, y: 10, width: 50, height: 200 } }, { boundingBox: { x: 200, y: 10, width: 50, height: 200 } }]; } };
    assert.match((await inspectImage(file(), 'passport')).error, /More than one face/);
    window.FaceDetector = class { async detect() { return [{ boundingBox: { x: 260, y: 40, width: 55, height: 70 } }]; } };
    assert.match((await inspectImage(file(), 'passport')).error, /too small/);
    window.FaceDetector = class { async detect() { return [{ boundingBox: { x: 210, y: 80, width: 180, height: 230 } }]; } };
    assert.equal((await inspectImage(file(), 'passport')).error, undefined, 'clear head-and-shoulders framing stays eligible for review');
    window.FaceDetector = class { async detect() { return [{ boundingBox: { x: 180, y: 80, width: 240, height: 390 } }]; } };
    assert.match((await inspectImage(file(), 'aadhaar')).error, /portrait rather than an Aadhaar/);
    window.FaceDetector = class { async detect() { return []; } };
    window.TextDetector = class { async detect() { return [{ rawValue: 'INCOME TAX DEPARTMENT PAN CARD' }]; } };
    assert.match((await inspectImage(file(), 'aadhaar')).error, /different document/);
    delete window.TextDetector;
    assert.match((await inspectImage(file(), 'aadhaar')).warning, /admin/);
    globalThis.createImageBitmap = async () => { throw new Error('decode failed'); };
    assert.match((await inspectImage(file(), 'aadhaar')).error, /damaged or unreadable/);
    globalThis.createImageBitmap = async () => ({ width: 600, height: 750, close() {} });
    globalThis.document = { createElement: () => ({ getContext: () => ({ drawImage() {}, getImageData: () => ({ data: new Uint8ClampedArray(64 * 64 * 4) }) }) }) };
    assert.match((await inspectImage(file(), 'aadhaar')).error, /severely blurred/);
    const filtered = new Uint8ClampedArray(64 * 64 * 4);
    for (let i = 0; i < filtered.length; i += 4) { filtered[i] = (i / 4) % 2 ? 255 : 20; filtered[i + 1] = 5; filtered[i + 2] = 5; filtered[i + 3] = 255; }
    globalThis.document = { createElement: () => ({ getContext: () => ({ drawImage() {}, getImageData: () => ({ data: filtered }) }) }) };
    assert.match((await inspectImage(file(), 'passport')).warning, /Heavy color effects/);
  } finally { delete globalThis.window; delete globalThis.createImageBitmap; delete globalThis.document; }
});
