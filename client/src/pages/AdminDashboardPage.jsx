import { useEffect, useMemo, useState } from 'react';
import { Link, useNavigate } from 'react-router-dom';
import AppShell from '../components/AppShell.jsx';
import { api } from '../api/client.js';
import { memberIdDisplay } from '../api/memberId.js';

export default function AdminDashboardPage() {
  const navigate = useNavigate();
  const [dashboard, setDashboard] = useState({ totalUsers: 0, paymentPendingUsers: 0, districtCounts: [] });
  const [districts, setDistricts] = useState([]);
  const [users, setUsers] = useState([]);
  const [districtFilter, setDistrictFilter] = useState('');
  const [newDistrict, setNewDistrict] = useState({ name: '', whatsAppGroupLink: '' });
  const [message, setMessage] = useState('');
  const [error, setError] = useState('');
  const [exporting, setExporting] = useState(false);
  const readyCards = users.filter((user) => user.idCardStatus === 'Ready').length;

  const countMap = useMemo(
    () => Object.fromEntries(dashboard.districtCounts.map((item) => [item.district, item.count])),
    [dashboard.districtCounts]
  );

  async function loadData(filter = districtFilter) {
    const [dashboardResult, districtsResult, usersResult] = await Promise.all([
      api.dashboard(),
      api.getDistricts(),
      api.adminUsers(filter)
    ]);
    setDashboard(dashboardResult);
    setDistricts(districtsResult);
    setUsers(usersResult);
  }

  useEffect(() => {
    loadData().catch((err) => setError(err.message));
  }, []);

  async function handleFilterChange(event) {
    const value = event.target.value;
    setDistrictFilter(value);
    setError('');
    try {
      setUsers(await api.adminUsers(value));
    } catch (err) {
      setError(err.message);
    }
  }

  async function updateDistrict(district) {
    setError('');
    setMessage('');
    try {
      await api.updateDistrict(district.id, {
        name: district.name,
        whatsAppGroupLink: district.whatsAppGroupLink
      });
      setMessage('District WhatsApp group link updated.');
      await loadData();
    } catch (err) {
      setError(err.message);
    }
  }

  async function createDistrict(event) {
    event.preventDefault();
    setError('');
    setMessage('');
    try {
      await api.createDistrict(newDistrict);
      setNewDistrict({ name: '', whatsAppGroupLink: '' });
      setMessage('District added.');
      await loadData();
    } catch (err) {
      setError(err.message);
    }
  }

  function logout() {
    localStorage.removeItem('adminToken');
    navigate('/admin/login');
  }

  async function exportUsers() {
    setError('');
    setMessage('');
    setExporting(true);
    try {
      const result = await api.exportUsers();
      const url = URL.createObjectURL(result.blob);
      const link = document.createElement('a');
      link.href = url;
      link.download = result.fileName;
      document.body.appendChild(link);
      link.click();
      link.remove();
      URL.revokeObjectURL(url);
      setMessage('Users export downloaded.');
    } catch (err) {
      setError(err.message || 'Unable to export users. Please try again.');
    } finally {
      setExporting(false);
    }
  }

  return (
    <AppShell admin>
      <section className="admin-layout">
        <div className="admin-heading dashboard-hero">
          <div>
            <p className="eyebrow">Admin Dashboard</p>
            <h1>Welding Thozhilalar Sangam registrations</h1>
            <small>Monitor member registrations and manage district WhatsApp links.</small>
          </div>
          <button className="secondary" onClick={logout}>Logout</button>
        </div>

        {message && <div className="notice success">{message}</div>}
        {error && <div className="notice error">{error}</div>}

        <div className="stats-grid">
          <div className="stat-card featured">
            <span>Total Registered Members</span>
            <strong>{dashboard.totalUsers}</strong>
            <small>All submitted registrations</small>
          </div>
          <div className="stat-card">
            <span>ID Cards Ready</span>
            <strong>{readyCards}</strong>
            <small>Visible member ID cards</small>
          </div>
          <div className="stat-card">
            <span>Districts</span>
            <strong>{districts.length}</strong>
            <small>WhatsApp groups configured</small>
          </div>
          <div className="stat-card">
            <span>Visible Users</span>
            <strong>{users.length}</strong>
            <small>{districtFilter || 'All districts'}</small>
          </div>
          {dashboard.districtCounts.slice(0, 5).map((item) => (
            <div className="stat-card" key={item.district}>
              <span>{item.district}</span>
              <strong>{item.count}</strong>
              <small>Registered members</small>
            </div>
          ))}
        </div>

        <section className="panel data-panel">
          <div className="section-title">
            <div>
              <p className="eyebrow">Members</p>
              <h2>User List</h2>
            </div>
            <div className="table-actions">
              <select value={districtFilter} onChange={handleFilterChange}>
                <option value="">All districts</option>
                {districts.map((district) => (
                  <option key={district.id} value={district.name}>
                    {district.name}
                  </option>
                ))}
              </select>
              <button className="secondary" disabled={exporting} onClick={exportUsers}>
                {exporting ? 'Exporting...' : 'Export Users'}
              </button>
            </div>
          </div>
          <div className="table-wrap">
            <table>
              <thead>
                <tr>
                  <th>Name</th>
                  <th>Phone</th>
                  <th>District</th>
                  <th>Member ID</th>
                  <th>Registered</th>
                  <th>ID Card Status</th>
                  <th>Details</th>
                </tr>
              </thead>
              <tbody>
                {users.map((user) => (
                  <tr key={user.id}>
                    <td>{user.name}</td>
                    <td>{user.phoneNumber}</td>
                    <td>{user.district}</td>
                    <td>{memberIdDisplay(user) || '-'}{user.memberIdDisplayWarning && <small role="status">{user.memberIdDisplayWarning}</small>}</td>
                    <td>{new Date(user.createdAt).toLocaleDateString()}</td>
                    <td><span className="status-pill">{user.idCardStatus}</span></td>
                    <td><Link to={`/admin/users/${user.id}`}>View</Link></td>
                  </tr>
                ))}
                {users.length === 0 && (
                  <tr>
                    <td colSpan="7" className="empty">No users found.</td>
                  </tr>
                )}
              </tbody>
            </table>
          </div>
        </section>

        <section className="panel data-panel">
          <div className="section-title">
            <div>
              <p className="eyebrow">District Operations</p>
              <h2>Manage District WhatsApp Links</h2>
            </div>
          </div>
          <form className="district-create" onSubmit={createDistrict}>
            <input
              required
              placeholder="District name"
              value={newDistrict.name}
              onChange={(event) => setNewDistrict({ ...newDistrict, name: event.target.value })}
            />
            <input
              required
              type="url"
              placeholder="WhatsApp group link"
              value={newDistrict.whatsAppGroupLink}
              onChange={(event) => setNewDistrict({ ...newDistrict, whatsAppGroupLink: event.target.value })}
            />
            <button className="primary">Add</button>
          </form>
          <div className="district-list">
            {districts.map((district) => (
              <div className="district-row" key={district.id}>
                <span>{district.name}</span>
                <span>{countMap[district.name] || 0} users</span>
                <input
                  value={district.whatsAppGroupLink}
                  onChange={(event) =>
                    setDistricts((current) =>
                      current.map((item) =>
                        item.id === district.id ? { ...item, whatsAppGroupLink: event.target.value } : item
                      )
                    )
                  }
                />
                <button className="secondary" onClick={() => updateDistrict(district)}>Save</button>
              </div>
            ))}
          </div>
        </section>
      </section>
    </AppShell>
  );
}
