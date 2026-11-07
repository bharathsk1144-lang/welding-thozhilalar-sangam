import { branding } from '../config/branding.js';
import { useState } from 'react';
import { useNavigate } from 'react-router-dom';
import AppShell from '../components/AppShell.jsx';
import { api } from '../api/client.js';

export default function AdminLoginPage() {
  const navigate = useNavigate();
  const [form, setForm] = useState({ username: '', password: '' });
  const [error, setError] = useState('');
  const [loading, setLoading] = useState(false);

  async function handleSubmit(event) {
    event.preventDefault();
    setError('');
    setLoading(true);
    try {
      const result = await api.adminLogin(form);
      localStorage.setItem('adminToken', result.token);
      navigate('/admin/dashboard');
    } catch (err) {
      setError(err.message);
    } finally {
      setLoading(false);
    }
  }

  return (
    <AppShell>
      <section className="auth-wrap">
        <form className="panel auth-card" onSubmit={handleSubmit}>
          <img src={branding.logo} alt="Welding Thozhilalar Sangam logo" className="auth-logo" />
          <p className="eyebrow">Admin Portal</p>
          <h1>Admin login</h1>
          <small>Secure access for Welding Thozhilalar Sangam administrators.</small>
          {error && <div className="notice error">{error}</div>}
          <label>
            <span>Username or Email / பயனர் பெயர் அல்லது மின்னஞ்சல்</span>
            <input
              required
              value={form.username}
              onChange={(event) => setForm({ ...form, username: event.target.value })}
              placeholder="admin@wts.com"
            />
          </label>
          <label>
            <span>Password / கடவுச்சொல்</span>
            <input
              required
              type="password"
              value={form.password}
              onChange={(event) => setForm({ ...form, password: event.target.value })}
              placeholder="Admin password"
            />
          </label>
          <button disabled={loading} className="primary">Login</button>
        </form>
      </section>
    </AppShell>
  );
}
