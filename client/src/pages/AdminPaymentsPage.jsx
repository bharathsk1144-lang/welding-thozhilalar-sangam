import { useEffect, useState } from 'react';
import { Link } from 'react-router-dom';
import AppShell from '../components/AppShell.jsx';
import { api, downloadPrivateDocument } from '../api/client.js';

export default function AdminPaymentsPage() {
  const [payments, setPayments] = useState([]);
  const [message, setMessage] = useState('');
  const [error, setError] = useState('');
  const [loadingId, setLoadingId] = useState('');

  async function loadPayments() {
    setPayments(await api.pendingPayments());
  }

  useEffect(() => {
    loadPayments().catch((err) => setError(err.message));
  }, []);

  async function handleAction(userId, action) {
    setError('');
    setMessage('');
    setLoadingId(userId);
    try {
      const result = action === 'verify' ? await api.verifyPayment(userId) : await api.rejectPayment(userId);
      setMessage(result.message);
      await loadPayments();
    } catch (err) {
      setError(err.message);
    } finally {
      setLoadingId('');
    }
  }

  return (
    <AppShell admin>
      <section className="admin-layout">
        <div className="admin-heading dashboard-hero">
          <div>
            <p className="eyebrow">Payment Verification</p>
            <h1>Pending payment approvals</h1>
            <small>Verify or reject submitted transaction details before ID cards are enabled.</small>
          </div>
          <Link to="/admin/dashboard" className="button-link secondary">Dashboard</Link>
        </div>

        {message && <div className="notice success">{message}</div>}
        {error && <div className="notice error">{error}</div>}

        <section className="panel data-panel">
          <div className="section-title">
            <div>
              <p className="eyebrow">Pending Users</p>
              <h2>Payment Pending Users</h2>
            </div>
          </div>

          <div className="payment-admin-list">
            {payments.map((payment) => (
              <article className="payment-review-card" key={payment.id}>
                <div>
                  <strong>{payment.name}</strong>
                  <small>{payment.district} · {payment.phoneNumber}</small>
                </div>
                <div>
                  <span>UTR / Transaction ID</span>
                  <strong>{payment.paymentTransactionId}</strong>
                </div>
                <div>
                  <span>Submitted</span>
                  <strong>{payment.paymentSubmittedAt ? new Date(payment.paymentSubmittedAt).toLocaleString() : '-'}</strong>
                </div>
                <div className="payment-actions">
                  {payment.paymentProofUrl && (
                    <button className="text-button" onClick={() => downloadPrivateDocument(payment.paymentProofUrl).catch((err) => setError(err.message))}>
                      Download proof
                    </button>
                  )}
                  <Link to={`/admin/users/${payment.id}`}>User details</Link>
                  <button
                    className="primary"
                    disabled={loadingId === payment.id}
                    onClick={() => handleAction(payment.id, 'verify')}
                  >
                    Verify
                  </button>
                  <button
                    className="secondary danger-action"
                    disabled={loadingId === payment.id}
                    onClick={() => handleAction(payment.id, 'reject')}
                  >
                    Reject
                  </button>
                </div>
              </article>
            ))}

            {payments.length === 0 && <div className="empty-state">No pending payment submissions.</div>}
          </div>
        </section>
      </section>
    </AppShell>
  );
}
