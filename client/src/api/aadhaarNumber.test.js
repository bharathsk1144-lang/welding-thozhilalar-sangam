import test from 'node:test';
import assert from 'node:assert/strict';
import { validAadhaarNumber } from './aadhaarNumber.js';

test('Aadhaar requires twelve ASCII digits and a valid Verhoeff checksum', () => {
  assert.equal(validAadhaarNumber('234567890124'), true);
  for (const invalid of ['', '2345678901', '2345678901245', '23456789012A', '23456789012!',
    '234567890125', '134567890124', '٢٣٤٥٦٧٨٩٠١٢٤'])
    assert.equal(validAadhaarNumber(invalid), false);
});
