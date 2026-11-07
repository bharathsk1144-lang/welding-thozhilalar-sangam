export const STATISTICS_CHANGED = 'wts:membership-statistics-changed';
export const STATISTICS_STORAGE_KEY = 'wtsStatisticsUpdatedAt';

export async function fetchPublicStatistics(apiBase, { signal, fetchImpl = fetch } = {}) {
  const response = await fetchImpl(`${apiBase}/statistics`, {
    signal, cache: 'no-store', credentials: 'omit', headers: { Accept: 'application/json' }
  });
  if (!response.ok) throw new Error('Membership statistics are temporarily unavailable.');
  const data = await response.json();
  if (!data || !['totalRegisteredMembers', 'configuredDistricts'].every(key =>
    Number.isSafeInteger(data[key]) && data[key] >= 0)) {
    throw new Error('Membership statistics are temporarily unavailable.');
  }
  return { totalRegisteredMembers: data.totalRegisteredMembers, configuredDistricts: data.configuredDistricts };
}

// Registration response data is never part of this notification.
export function notifyStatisticsChanged(target = window, storage) {
  target.dispatchEvent(new Event(STATISTICS_CHANGED));
  try { (storage ?? target.localStorage).setItem(STATISTICS_STORAGE_KEY, String(Date.now())); } catch { /* Storage may be disabled. */ }
}

export function observeStatistics({ load, onChange, target = window, timeoutMs = 10000 }) {
  let request, version = 0, disposed = false;
  async function refresh() {
    if (disposed) return;
    const current = ++version;
    request?.abort();
    const controller = new AbortController();
    request = controller;
    onChange({ loading: true, data: null, error: false });
    const timeout = setTimeout(() => controller.abort(), timeoutMs);
    try {
      const data = await load(controller.signal);
      if (!disposed && current === version) onChange({ loading: false, data, error: false });
    } catch {
      if (!disposed && current === version) onChange({ loading: false, data: null, error: true });
    } finally { clearTimeout(timeout); }
  }
  const storageChanged = event => { if (event.key === STATISTICS_STORAGE_KEY) void refresh(); };
  const events = ['focus', 'pageshow', STATISTICS_CHANGED];
  events.forEach(name => target.addEventListener(name, refresh));
  target.addEventListener('storage', storageChanged);
  void refresh();
  return {
    refresh,
    dispose() {
      disposed = true;
      request?.abort();
      events.forEach(name => target.removeEventListener(name, refresh));
      target.removeEventListener('storage', storageChanged);
    }
  };
}
