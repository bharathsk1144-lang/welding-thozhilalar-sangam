// Local format and Verhoeff checksum validation only; this cannot verify identity.
const D = [
  [0,1,2,3,4,5,6,7,8,9], [1,2,3,4,0,6,7,8,9,5], [2,3,4,0,1,7,8,9,5],
  [3,4,0,1,2,8,9,5,6,7], [4,0,1,2,3,9,5,6,7,8], [5,9,8,7,6,0,4,3,2,1],
  [6,5,9,8,7,1,0,4,3,2], [7,6,5,9,8,2,1,0,4,3], [8,7,6,5,9,3,2,1,0,4],
  [9,8,7,6,5,4,3,2,1,0]
];
const P = [
  [0,1,2,3,4,5,6,7,8,9], [1,5,7,6,2,8,3,0,9,4], [5,8,0,3,7,9,6,1,4,2],
  [8,9,1,6,0,4,3,5,2,7], [9,4,5,3,1,2,6,8,7,0], [4,2,8,6,5,7,3,9,0,1],
  [2,7,9,3,8,0,6,4,1,5], [7,0,4,6,9,1,3,2,5,8]
];

export function validAadhaarNumber(value) {
  if (!/^[2-9][0-9]{11}$/.test(value || '')) return false;
  let check = 0;
  for (let index = 0; index < 12; index++) check = D[check][P[index % 8][Number(value[11 - index])]];
  return check === 0;
}

export const AADHAAR_HINT = 'சரியான 12 இலக்க ஆதார் எண்ணை உள்ளிடவும் / Enter a valid 12-digit Aadhaar number.';
