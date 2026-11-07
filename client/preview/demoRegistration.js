// This module is reachable only from the isolated client-preview entry point.
export function normalizeDemoMobile(value) {
  const phone = String(value || '').trim().replace(/^(?:\+91|91)(?=[6-9][0-9]{9}$)/, '');
  return /^[6-9][0-9]{9}$/.test(phone) ? phone : '';
}
export function validateDemoIdentity(name, phone) {
  return !String(name || '').trim() || String(name).trim().length > 100
    ? 'Enter a sample name (up to 100 characters).'
    : !normalizeDemoMobile(phone) ? 'Enter a valid sample Indian mobile number.' : '';
}
export const sampleCard = Object.freeze({
  name: 'SAMPLE MEMBER · DEMO', memberId: 'DEMO-NOT-VALID', district: 'Chennai',
  phoneNumber: '0000000000', dateOfBirth: '1990-01-01', fatherName: 'Sample Father',
  bloodGroup: 'O+', permanentAddress: 'SYNTHETIC SAMPLE ADDRESS — NOT A REAL HOME',
  issueDate: '2026-01-01', validityYear: 'DEMO ONLY'
});
