export function formatDobInput(value) {
  const digits = String(value).replace(/\D/g, '').slice(0, 8);
  return [digits.slice(0, 2), digits.slice(2, 4), digits.slice(4)].filter(Boolean).join('-');
}

export function dobToIso(value) {
  const match = /^(\d{2})-(\d{2})-(\d{4})$/.exec(value);
  if (!match) return null;
  const [, dd, mm, yyyy] = match;
  const year = Number(yyyy), month = Number(mm), day = Number(dd);
  const date = new Date(Date.UTC(year, month - 1, day));
  if (year < 1900 || date.getUTCFullYear() !== year || date.getUTCMonth() !== month - 1 || date.getUTCDate() !== day || date >= new Date()) return null;
  return `${yyyy}-${mm}-${dd}`;
}

export function isoToDob(value) {
  const match = /^(\d{4})-(\d{2})-(\d{2})/.exec(value || '');
  return match ? `${match[3]}-${match[2]}-${match[1]}` : '';
}
