export function resolveApiBase(configured, location) {
  if (configured?.trim()) return configured.trim().replace(/\/$/, '');
  const hostname = location?.hostname || 'localhost';
  const host = hostname.includes(':') && !hostname.startsWith('[') ? `[${hostname}]` : hostname;
  return `${location?.protocol === 'https:' ? 'https:' : 'http:'}//${host}:5087/api`;
}
