import { branding } from '../config/branding.js';
import { useEffect, useRef, useState } from 'react';
import { Link, useNavigate } from 'react-router-dom';
import AppShell from '../components/AppShell.jsx';
import MembershipIdCard, { downloadIdCard } from '../components/MembershipIdCard.jsx';
import { api } from '../api/client.js';
import { memberIdDisplay } from '../api/memberId.js';

export default function MyIdCardPage() {
  const navigate = useNavigate();
  const cardRef = useRef(null);
  const [card, setCard] = useState(null);
  const [error, setError] = useState('');
  const [message, setMessage] = useState('');
  const [downloadError, setDownloadError] = useState('');
  const [loading, setLoading] = useState(true);
  const [downloading, setDownloading] = useState(false);

  useEffect(() => {
    const session = getUserSession();
    if (!session?.userId) {
      navigate('/user-login', { replace: true });
      return;
    }

    setLoading(true);
    api.idCard(session.userId)
      .then((result) => {
        setCard(result);
        localStorage.setItem('wtsUser', JSON.stringify({
          userId: result.id,
          phone: result.phoneNumber,
          memberId: result.memberId
        }));
      })
      .catch((err) => setError(err.message || 'Unable to load your ID card. Please try again.'))
      .finally(() => setLoading(false));
  }, [navigate]);

  async function handleDownload() {
    setDownloading(true);
    setMessage('');
    setDownloadError('');
    setError('');
    try {
      const result = await downloadIdCard(cardRef.current, memberIdDisplay(card));
      setMessage(
        result?.type === 'png'
          ? 'PDF download failed. A PNG with both sides was downloaded instead.'
          : 'Both sides of your ID card downloaded successfully.'
      );
    } catch (err) {
      setDownloadError(`ID card download failed. Please try again or use Print. ${err.message || ''}`.trim());
    } finally {
      setDownloading(false);
    }
  }

  function handleLogout() {
    localStorage.removeItem('wtsUser');
    sessionStorage.removeItem('wtsUser');
    sessionStorage.removeItem('wtsUserToken');
    navigate('/', { replace: true });
  }

  return (
    <AppShell>
      <section className="id-card-wrap">
        {downloadError && <div className="notice error" role="alert">{downloadError}</div>}
        {loading && (
          <div className="panel unavailable-card">
            <img src={branding.logo} alt="" className="auth-logo" />
            <p className="eyebrow">My ID Card</p>
            <h1>Loading ID card</h1>
            <p>Please wait while we fetch your latest membership details.</p>
          </div>
        )}

        {error && (
          <div className="panel unavailable-card">
            <img src={branding.logo} alt="" className="auth-logo" />
            <p className="eyebrow">My ID Card</p>
            <h1>ID card unavailable</h1>
            <p>{error}</p>
            <div className="id-actions">
              <Link to="/user-login" className="button-link primary">Login Again</Link>
              <Link to="/" className="button-link secondary">Back to Home</Link>
            </div>
          </div>
        )}

        {!loading && !error && !card && (
          <div className="panel unavailable-card">
            <img src={branding.logo} alt="" className="auth-logo" />
            <p className="eyebrow">My ID Card</p>
            <h1>No ID card found</h1>
            <p>No registration was found for this login. Please register first.</p>
            <div className="id-actions">
              <Link to="/register" className="button-link primary">Register</Link>
              <button className="secondary" onClick={handleLogout}>Logout</button>
            </div>
          </div>
        )}

        {!loading && !error && card && (
          <>
            <div className="notice success id-success">
              Welcome back. Your Membership ID Card is ready.
            </div>
            {message && <div className="notice success id-success">{message}</div>}
            <div className="id-actions">
              <button className="primary" disabled={downloading} onClick={handleDownload}>
                {downloading ? 'Preparing Download...' : 'Download ID Card'}
              </button>
              <Link to="/" className="button-link secondary">Back to Home</Link>
              <button className="secondary" onClick={handleLogout}>Logout</button>
            </div>

            <MembershipIdCard ref={cardRef} card={card} />
          </>
        )}
      </section>
    </AppShell>
  );
}

function getUserSession() {
  try {
    return JSON.parse(localStorage.getItem('wtsUser') || sessionStorage.getItem('wtsUser') || 'null');
  } catch {
    return null;
  }
}
