export function resolveApiBase(configured, location) {
  if (configured?.trim()) return configured.trim().replace(/\/$/, '');
  const hostname = location?.hostname || 'localhost';
  if (hostname === 'www.weldingthozhilalarsangam.com' || hostname === 'weldingthozhilalarsangam.com')
    return 'https://welding-thozhilalar-sangam.onrender.com/api';
  const host = hostname.includes(':') && !hostname.startsWith('[') ? `[${hostname}]` : hostname;
  return `${location?.protocol === 'https:' ? 'https:' : 'http:'}//${host}:5087/api`;
}
