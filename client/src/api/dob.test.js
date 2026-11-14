import test from 'node:test';
import assert from 'node:assert/strict';
import { dobToIso, formatDobInput, isoToDob } from './dob.js';

test('DOB typing and picker use DD-MM-YYYY without timezone conversion', () => {
  assert.equal(formatDobInput('29022000'), '29-02-2000');
  assert.equal(dobToIso('29-02-2000'), '2000-02-29');
  assert.equal(isoToDob('2000-02-29T00:00:00Z'), '29-02-2000');
  assert.equal(dobToIso('29-02-2001'), null);
  assert.equal(dobToIso('31-04-1990'), null);
  assert.equal(dobToIso('01-01-2999'), null);
});
