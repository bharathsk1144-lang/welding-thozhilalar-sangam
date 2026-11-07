import test from 'node:test';
import assert from 'node:assert/strict';
import { normalizeMobile, MOBILE_PATTERN } from './mobile.js';

test('all supported Indian formats resolve to the same account', () => {
  for (const input of ['9876543210', '+919876543210', '919876543210', ' 9876543210 ']) {
    assert.equal(normalizeMobile(input), '9876543210');
  }
});

test('reject invalid numbers instead of stripping arbitrary characters', () => {
  for (const input of ['', null, '1234567890', '987654321', '98765432100', '98765 43210', '９８７６５４３２１０', '+00919876543210', '9876543210abc']) {
    assert.equal(normalizeMobile(input), null);
  }
});

test('HTML input accepts the three supported formats', () => {
  const pattern = new RegExp(`^(?:${MOBILE_PATTERN})$`, 'v');
  for (const input of ['9876543210', '+919876543210', '919876543210']) assert.ok(pattern.test(input));
  for (const input of ['1234567890', '+9198765432100', '91987654321']) assert.ok(!pattern.test(input));
});
