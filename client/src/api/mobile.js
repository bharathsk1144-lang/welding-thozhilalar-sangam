export function normalizeMobile(value) {
  let mobile = (value || '').trim();
  if (mobile.startsWith('+91')) mobile = mobile.slice(3);
  else if (mobile.length === 12 && mobile.startsWith('91')) mobile = mobile.slice(2);
  return /^[6-9][0-9]{9}$/.test(mobile) ? mobile : null;
}

export const MOBILE_PATTERN = '(\\+91|91)?[6-9][0-9]{9}';
