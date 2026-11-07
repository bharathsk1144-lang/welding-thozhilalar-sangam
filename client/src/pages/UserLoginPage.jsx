import { branding } from '../config/branding.js';
import { useState } from 'react';
import { Link, useNavigate } from 'react-router-dom';
import AppShell from '../components/AppShell.jsx';
import { api } from '../api/client.js';
import { normalizeMobile, MOBILE_PATTERN } from '../api/mobile.js';

export default function UserLoginPage() {
  const navigate = useNavigate();
  const [phoneNumber, setPhoneNumber] = useState('');
  const [password, setPassword] = useState('');
  const [error, setError] = useState('');
  const [loading, setLoading] = useState(false);

  async function handleLogin(event) {
    event.preventDefault();
    setError('');

    const phone = normalizeMobile(phoneNumber);
    if (!phone) {
      setError('Enter a valid Indian mobile number, optionally prefixed by +91 or 91.');
      return;
    }

    if (!password) {
      setError('Password is required.');
      return;
    }

    setLoading(true);
    try {
      const loginResult = await api.userLogin({
        phone,
        password
      });

      sessionStorage.setItem('wtsUserToken', loginResult.token);
      localStorage.setItem('wtsUser', JSON.stringify({
        userId: loginResult.userId,
        phone: loginResult.phone,
        memberId: loginResult.memberId
      }));

      navigate('/my-id-card', { replace: true });
    } catch (err) {
      setError(err.message || 'Invalid mobile number or password.');
    } finally {
      setLoading(false);
    }
  }

  return (
    <AppShell>
      <section className="auth-wrap">
        <div className="panel auth-card user-login-card">
          <img src={branding.logo} alt="" className="auth-logo" />
          <p className="eyebrow">User Login</p>
          <h1>User Login</h1>
          <p className="auth-copy">Login using your registered mobile number and password.</p>

          {error && <div className="notice error">{error}</div>}

          <form className="form-grid" onSubmit={handleLogin}>
            <label>
              <span>Mobile Number / தொலைபேசி எண்</span>
              <input
                required
                inputMode="numeric"
                value={phoneNumber}
                onChange={(event) => setPhoneNumber(event.target.value)}
                placeholder="10 digit mobile number"
                pattern={MOBILE_PATTERN}
                title="Enter a valid 10 digit mobile number"
              />
            </label>
            <label>
              <span>Password</span>
              <input
                required
                type="password"
                value={password}
                onChange={(event) => setPassword(event.target.value)}
                placeholder="Enter password"
              />
            </label>
            <button className="primary" disabled={loading}>
              {loading ? 'Logging in...' : 'Login'}
            </button>
          </form>

          <small>
            <Link to="/forgot-password">Forgot Password?</Link>
          </small>
          <small>
            No registration found? <Link to="/register">Please register first.</Link>
          </small>
        </div>
      </section>
    </AppShell>
  );
}
