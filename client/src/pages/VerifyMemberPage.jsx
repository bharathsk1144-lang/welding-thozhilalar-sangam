import { branding } from '../config/branding.js';
import { useState } from 'react';
import { Link } from 'react-router-dom';
import AppShell from '../components/AppShell.jsx';
import { api } from '../api/client.js';
import { memberIdDisplay } from '../api/memberId.js';

export default function VerifyMemberPage() {
  const [memberId, setMemberId] = useState('');
  const [result, setResult] = useState(null);
  const [error, setError] = useState('');
  const [loading, setLoading] = useState(false);

  async function handleVerify(event) {
    event.preventDefault();
    setError('');
    setResult(null);

    if (!memberId.trim()) {
      setError('Member ID is required.');
      return;
    }

    setLoading(true);
    try {
      setResult(await api.verifyMember(memberId.trim()));
    } catch (err) {
      setError(err.message || 'Unable to verify this Member ID. Please check and try again.');
    } finally {
      setLoading(false);
    }
  }

  return (
    <AppShell>
      <section className="auth-wrap verify-wrap">
        <div className="panel auth-card verify-card">
          <img src={branding.logo} alt="" className="auth-logo" />
          <p className="eyebrow">Verify Member</p>
          <h1>Member verification</h1>
          <p className="auth-copy">
            Enter a Welding Thozhilalar Sangam Member ID to verify safe public membership status.
          </p>

          <form className="form-grid" onSubmit={handleVerify}>
            <label>
              <span>Member ID / உறுப்பினர் எண்</span>
              <input
                required
                value={memberId}
                onChange={(event) => setMemberId(event.target.value)}
                placeholder="WTS-2026-XXXXXX"
              />
            </label>
            <button className="primary" disabled={loading}>
              {loading ? 'Verifying...' : 'Verify'}
            </button>
          </form>

          {error && <div className="notice error">{error}</div>}

          {result && (
            <article className="public-verification-card">
              <div className="verified-seal">Verified Member</div>
              <div>
                <p className="eyebrow">Welding Thozhilalar Sangam</p>
                <h2>{result.name || '-'}</h2>
              </div>
              <div className="verification-grid">
                <VerificationField label="Member ID" value={memberIdDisplay(result)} />
                {memberIdDisplay(result) !== result.memberId && <VerificationField label="Original printed Member ID" value={result.memberId} />}
                <VerificationField label="District" value={result.district} />
                <VerificationField label="Status" value={result.status || 'Active Member'} />
                <VerificationField label="Verification" value={result.verificationStatus || 'Verified'} />
                <VerificationField label="Validity" value={result.validity} />
              </div>
              <p>{result.message || 'Verified Welding Thozhilalar Sangam member.'}</p>
            </article>
          )}

          <Link to="/" className="button-link secondary">Back to Home</Link>
        </div>
      </section>
    </AppShell>
  );
}

function VerificationField({ label, value }) {
  return (
    <div className="detail">
      <span>{label}</span>
      <strong>{value || '-'}</strong>
    </div>
  );
}
