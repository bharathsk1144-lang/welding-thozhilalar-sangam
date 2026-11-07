import { branding } from '../config/branding.js';
import { useEffect, useRef, useState } from 'react';
import { Link, useParams } from 'react-router-dom';
import AppShell from '../components/AppShell.jsx';
import MembershipIdCard, { downloadIdCard } from '../components/MembershipIdCard.jsx';
import { api } from '../api/client.js';
import { memberIdDisplay } from '../api/memberId.js';

export default function IdCardPage() {
  const { userId } = useParams();
  const cardRef = useRef(null);
  const [card, setCard] = useState(null);
  const [error, setError] = useState('');
  const [loading, setLoading] = useState(true);
  const [downloadMessage, setDownloadMessage] = useState('');
  const [downloadError, setDownloadError] = useState('');
  const [downloading, setDownloading] = useState(false);

  useEffect(() => {
    setLoading(true);
    setError('');
    api.idCard(userId)
      .then((result) => {
        setCard(result);
      })
      .catch((err) => setError(err.message || 'Unable to load ID card. Please try again.'))
      .finally(() => setLoading(false));
  }, [userId]);

  async function handleDownload() {
    setDownloading(true);
    setDownloadMessage('');
    setDownloadError('');
    setError('');
    try {
      const result = await downloadIdCard(cardRef.current, memberIdDisplay(card));
      setDownloadMessage(
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

  return (
    <AppShell>
      <section className="id-card-wrap">
        {downloadError && <div className="notice error" role="alert">{downloadError}</div>}
        {loading && (
          <div className="panel unavailable-card">
            <img src={branding.logo} alt="" className="auth-logo" />
            <p className="eyebrow">ID Card Status</p>
            <h1>Loading ID card</h1>
            <p>Please wait while we fetch the latest membership details.</p>
          </div>
        )}

        {error && (
          <div className="panel unavailable-card">
            <img src={branding.logo} alt="" className="auth-logo" />
            <p className="eyebrow">ID Card Status</p>
            <h1>ID card unavailable</h1>
            <p>{error}</p>
            <small>Please check the registration reference, or start backend if the server is offline.</small>
            <Link to="/" className="button-link primary">Back to Home</Link>
          </div>
        )}

        {!loading && !error && !card && (
          <div className="panel unavailable-card">
            <img src={branding.logo} alt="" className="auth-logo" />
            <p className="eyebrow">ID Card Status</p>
            <h1>No ID card found</h1>
            <p>We could not find a membership card for this registration reference.</p>
            <Link to="/" className="button-link primary">Back to Home</Link>
          </div>
        )}

        {!loading && !error && card && (
          <>
            <div className="notice success id-success">
              Registration completed successfully. Your Membership ID Card is ready.
            </div>
            {downloadMessage && <div className="notice success id-success">{downloadMessage}</div>}
            <div className="id-actions">
              <button className="primary" disabled={downloading} onClick={handleDownload}>
                {downloading ? 'Preparing Download...' : 'Download ID Card'}
              </button>
              <button className="secondary" onClick={() => window.print()}>Print</button>
              <Link to="/" className="button-link secondary">Back to Home</Link>
            </div>

            <MembershipIdCard ref={cardRef} card={card} />
          </>
        )}
      </section>
    </AppShell>
  );
}
