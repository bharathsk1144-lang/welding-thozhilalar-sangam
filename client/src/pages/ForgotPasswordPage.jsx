import { branding } from '../config/branding.js';
import { useState } from 'react';
import { Link, useNavigate } from 'react-router-dom';
import AppShell from '../components/AppShell.jsx';
import { api } from '../api/client.js';
import { normalizeMobile, MOBILE_PATTERN } from '../api/mobile.js';

export default function ForgotPasswordPage() {
  const navigate = useNavigate();
  const [step, setStep] = useState('phone');
  const [phoneNumber, setPhoneNumber] = useState('');
  const [otp, setOtp] = useState('');
  const [verificationToken, setVerificationToken] = useState('');
  const [password, setPassword] = useState('');
  const [confirmPassword, setConfirmPassword] = useState('');
  const [message, setMessage] = useState('');
  const [error, setError] = useState('');
  const [loading, setLoading] = useState(false);

  async function handleSendOtp(event) {
    event.preventDefault();
    setError('');
    setMessage('');

    const phone = normalizeMobile(phoneNumber);
    if (!phone) {
      setError('Enter a valid Indian mobile number, optionally prefixed by +91 or 91.');
      return;
    }

    setLoading(true);
    try {
      const result = await api.forgotPasswordSendOtp({ phone });
      setPhoneNumber(phone);
      setStep('otp');
      setMessage(result.developmentOtp ? `OTP request successful. Demo OTP is ${result.developmentOtp}.` : 'OTP sent successfully.');
    } catch (err) {
      setError(err.message || 'Unable to send OTP. Please try again.');
    } finally {
      setLoading(false);
    }
  }

  async function handleVerifyOtp(event) {
    event.preventDefault();
    setError('');
    setMessage('');

    if (!/^\d{6}$/.test(otp.trim())) {
      setError('OTP must be 6 digits.');
      return;
    }

    setLoading(true);
    try {
      const result = await api.verifyOtp({ phoneNumber: phoneNumber.trim(), otp: otp.trim(), purpose: 'password-reset' });
      setVerificationToken(result.verificationToken);
      setStep('password');
      setMessage('OTP verified. Set your new password.');
    } catch (err) {
      setError(err.message || 'Unable to verify OTP. Please try again.');
    } finally {
      setLoading(false);
    }
  }

  async function handleResetPassword(event) {
    event.preventDefault();
    setError('');
    setMessage('');

    if (password.length < 8) {
      setError('Password must be at least 8 characters.');
      return;
    }

    if (password !== confirmPassword) {
      setError('Confirm password must match password.');
      return;
    }

    setLoading(true);
    try {
      const result = await api.forgotPasswordReset({
        phone: phoneNumber.trim(),
        verificationToken,
        newPassword: password
      });
      setMessage(result.message || 'Password reset successfully. Please login.');
      window.setTimeout(() => navigate('/user-login', { replace: true }), 900);
    } catch (err) {
      setError(err.message || 'Unable to reset password. Please try again.');
    } finally {
      setLoading(false);
    }
  }

  return (
    <AppShell>
      <section className="auth-wrap">
        <div className="panel auth-card user-login-card">
          <img src={branding.logo} alt="" className="auth-logo" />
          <p className="eyebrow">Forgot Password</p>
          <h1>Reset password</h1>
          <p className="auth-copy">Use OTP once to create a new password for your registered mobile number.</p>

          {message && <div className="notice success">{message}</div>}
          {error && <div className="notice error">{error}</div>}

          {step === 'phone' && (
            <form className="form-grid" onSubmit={handleSendOtp}>
              <label>
                <span>Mobile Number / தொலைபேசி எண்</span>
                <input
                  required
                  inputMode="numeric"
                  value={phoneNumber}
                  onChange={(event) => setPhoneNumber(event.target.value)}
                  placeholder="10 digit mobile number"
                  pattern={MOBILE_PATTERN}
                />
              </label>
              <button className="primary" disabled={loading}>
                {loading ? 'Sending OTP...' : 'Send OTP'}
              </button>
            </form>
          )}

          {step === 'otp' && (
            <form className="form-grid" onSubmit={handleVerifyOtp}>
              <label>
                <span>OTP / ஒருமுறை கடவுச்சொல்</span>
                <input
                  required
                  inputMode="numeric"
                  value={otp}
                  onChange={(event) => setOtp(event.target.value)}
                  placeholder="6 digit OTP"
                  pattern="[0-9]{6}"
                />
              </label>
              <button className="primary" disabled={loading}>
                {loading ? 'Verifying...' : 'Verify OTP'}
              </button>
            </form>
          )}

          {step === 'password' && (
            <form className="form-grid" onSubmit={handleResetPassword}>
              <label>
                <span>New Password</span>
                <input
                  required
                  type="password"
                  value={password}
                  onChange={(event) => setPassword(event.target.value)}
                  minLength={8}
                  placeholder="Minimum 8 characters"
                />
              </label>
              <label>
                <span>Confirm Password</span>
                <input
                  required
                  type="password"
                  value={confirmPassword}
                  onChange={(event) => setConfirmPassword(event.target.value)}
                  minLength={8}
                  placeholder="Re-enter password"
                />
              </label>
              <button className="primary" disabled={loading}>
                {loading ? 'Resetting...' : 'Reset Password'}
              </button>
            </form>
          )}

          <small>
            Remembered password? <Link to="/user-login">Login</Link>
          </small>
        </div>
      </section>
    </AppShell>
  );
}
