import { useState } from 'react';
import { Link, useParams } from 'react-router-dom';
import AppShell from '../components/AppShell.jsx';
import { api } from '../api/client.js';

export default function PaymentPage() {
  const { userId } = useParams();
  const [transactionId, setTransactionId] = useState('');
  const [paymentProof, setPaymentProof] = useState(null);
  const [message, setMessage] = useState('');
  const [error, setError] = useState('');
  const [loading, setLoading] = useState(false);

  async function handleSubmit(event) {
    event.preventDefault();
    setError('');
    setMessage('');

    if (!transactionId.trim()) {
      setError('Transaction ID / UTR number is required.');
      return;
    }

    const formData = new FormData();
    formData.append('userId', userId);
    formData.append('paymentTransactionId', transactionId.trim());
    if (paymentProof) {
      formData.append('paymentProof', paymentProof);
    }

    setLoading(true);
    try {
      const result = await api.submitPayment(formData);
      setMessage(result.message);
    } catch (err) {
      setError(err.message);
    } finally {
      setLoading(false);
    }
  }

  return (
    <AppShell>
      <section className="payment-layout">
        <div className="panel payment-panel">
          <div className="form-heading">
            <p className="eyebrow">Payment Pending</p>
            <h1>Complete membership payment</h1>
            <small>Scan the QR code, complete payment, and submit your UTR / transaction reference.</small>
          </div>

          {message && <div className="notice success">{message}</div>}
          {error && <div className="notice error">{error}</div>}

          <div className="payment-grid">
            <div className="qr-card">
              <img src="/payment-qr.png" alt="Payment QR code" />
              <strong>Scan and pay</strong>
              <small>Use any UPI app. Replace this QR image in production at client/public/payment-qr.png.</small>
            </div>

            <form className="payment-form" onSubmit={handleSubmit}>
              <div className="instruction-card">
                <strong>Payment instructions</strong>
                <span>1. Scan the QR code and complete payment.</span>
                <span>2. Copy the Transaction ID / UTR number from your payment app.</span>
                <span>3. Upload a screenshot if available, then submit for admin verification.</span>
              </div>

              <label>
                <span>Transaction ID / UTR number</span>
                <input
                  required
                  value={transactionId}
                  onChange={(event) => setTransactionId(event.target.value)}
                  placeholder="Enter payment reference"
                />
              </label>

              <label>
                <span>Payment screenshot upload optional</span>
                <input
                  type="file"
                  accept="image/*,.pdf"
                  onChange={(event) => setPaymentProof(event.target.files?.[0] || null)}
                />
                <small>Optional. JPG, PNG, WEBP, or PDF up to 5 MB.</small>
              </label>

              <button className="primary" disabled={loading}>Submit Payment Details</button>
              <Link to={`/id-card/${userId}`} className="button-link secondary">Check ID Card Status</Link>
            </form>
          </div>
        </div>
      </section>
    </AppShell>
  );
}
