import test from 'node:test';
import assert from 'node:assert/strict';
import { memberIdDisplay, memberIdFilePart } from './memberId.js';

test('display uses the server alias while preserving the stored lookup ID', () => {
  const member = Object.freeze({ memberId: 'WTS-2024-FD48EF', displayMemberId: 'WTS-REG-48/2026-FD48EF' });
  assert.equal(memberIdDisplay(member), 'WTS-REG-48/2026-FD48EF');
  assert.equal(member.memberId, 'WTS-2024-FD48EF');
});
test('older APIs, ambiguous aliases and missing IDs remain safe', () => {
  assert.equal(memberIdDisplay({ memberId: 'WTS-2024-FD48EF' }), 'WTS-2024-FD48EF');
  assert.equal(memberIdDisplay({ memberId: 'WTS-2024-FD48EF', displayMemberId: 'WTS-2024-FD48EF', memberIdDisplayWarning: 'Review required' }), 'WTS-2024-FD48EF');
  assert.equal(memberIdDisplay(null), '');
  assert.equal(memberIdDisplay({ memberId: '', displayMemberId: '' }), '');
});
test('display IDs produce safe filenames without changing visible separators', () => {
  const id = 'WTS-REG-48/2026-FD48EF';
  assert.equal(memberIdFilePart(id), 'WTS-REG-48_2026-FD48EF');
  assert.equal(id, 'WTS-REG-48/2026-FD48EF');
  assert.equal(memberIdFilePart(''), 'MEMBER');
});
