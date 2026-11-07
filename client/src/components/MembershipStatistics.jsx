import { useEffect, useRef, useState } from 'react';
import { resolveApiBase } from '../api/baseUrl.js';
import { fetchPublicStatistics, observeStatistics } from '../api/statistics.js';

const apiBase = resolveApiBase(import.meta.env.VITE_API_BASE_URL, window.location);
const number = new Intl.NumberFormat('en-IN');

export default function MembershipStatistics() {
  const [state, setState] = useState({ loading: true, data: null, error: false });
  const observer = useRef(null);
  useEffect(() => {
    const subscription = observeStatistics({
      load: signal => fetchPublicStatistics(apiBase, { signal }), onChange: setState
    });
    observer.current = subscription;
    return () => subscription.dispose();
  }, []);

  const display = key => state.data ? number.format(state.data[key]) : '—';
  return (
    <section className="membership-statistics" aria-label="Sangam membership statistics" aria-busy={state.loading}>
      <div className="membership-statistics__cards" aria-live="polite" aria-atomic="true">
        <article className="membership-statistic membership-statistic--members">
          <p lang="ta">உறுப்பினர் வலிமை</p>
          <strong data-statistic="members">{display('totalRegisteredMembers')}</strong>
          <h2>Total Registered Members</h2>
          <small>Confirmed eligible memberships. Test accounts excluded.</small>
        </article>
        <article className="membership-statistic">
          <p lang="ta">மாவட்ட இணைப்பு</p>
          <strong data-statistic="districts">{display('configuredDistricts')}</strong>
          <h2>Configured Tamil Nadu Districts</h2>
          <small>Districts available for member registration.</small>
        </article>
      </div>
      <div className="membership-statistics__status" role="status">
        <span>{state.loading ? 'Loading membership statistics…' : state.error ? 'Statistics are temporarily unavailable. Please try again.' : 'Live membership totals from Sangam records.'}</span>
        <button type="button" className="statistics-refresh" onClick={() => observer.current?.refresh()} disabled={state.loading}>
          {state.error ? 'Retry statistics' : 'Refresh statistics'}
        </button>
      </div>
    </section>
  );
}
