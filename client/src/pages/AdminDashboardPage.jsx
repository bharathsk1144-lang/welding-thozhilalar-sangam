import { useEffect, useMemo, useState } from 'react';
import { Link, useNavigate } from 'react-router-dom';
import AppShell from '../components/AppShell.jsx';
import { api } from '../api/client.js';
import { memberIdDisplay } from '../api/memberId.js';

export default function AdminDashboardPage() {
  const navigate = useNavigate();
  const [dashboard, setDashboard] = useState({ totalUsers: 0, paymentPendingUsers: 0, pendingApprovalCount: 0, districtCounts: [] });
  const [pendingApprovals, setPendingApprovals] = useState([]);
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
    const [dashboardResult, districtsResult, usersResult, pendingResult] = await Promise.all([
      api.dashboard(),
      api.getDistricts(),
      api.adminUsers(filter),
      api.adminPendingApprovals()
    ]);
    setDashboard(dashboardResult);
    setDistricts(districtsResult);
    setUsers(usersResult);
    setPendingApprovals(pendingResult);
  }

  async function decideApproval(id, decision) {
    setError(''); setMessage('');
    const note = decision === 'Correction requested' ? window.prompt('What should the member correct?', 'Please provide clearer documents.') : '';
    if (note === null) return;
    try {
      await api.decideMemberApproval(id, decision, note);
      await loadData();
      setMessage(`Member approval status: ${decision}.`);
    } catch (err) { setError(err.message); }
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
        <Link to="/admin/website-content" className="button-link secondary">Website Content</Link>
        <a href="#pending-approvals" className="button-link secondary" aria-label={`${dashboard.pendingApprovalCount || 0} pending registrations`}>Pending registrations <strong>{dashboard.pendingApprovalCount || 0}</strong></a>

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
          <div className="stat-card" role="status">
            <span>Pending Admin Approval</span>
            <strong>{dashboard.pendingApprovalCount || 0}</strong>
            <small>New registrations awaiting review</small>
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

        <section className="panel data-panel" id="pending-approvals">
          <div className="section-title"><div><p className="eyebrow">Admin notification</p><h2>Pending Registrations ({dashboard.pendingApprovalCount || 0})</h2></div></div>
          <div className="table-wrap"><table><thead><tr><th>Name</th><th>District</th><th>Submitted</th><th>Status</th><th>Actions</th></tr></thead><tbody>
            {pendingApprovals.map((member) => <tr key={member.id}><td>{member.name}</td><td>{member.district}</td><td>{new Date(member.createdAt).toLocaleDateString()}</td><td>{member.approvalStatus}</td><td><Link to={`/admin/users/${member.id}`}>View</Link>{' '}<button type="button" onClick={() => decideApproval(member.id, 'Approved')}>Approve</button>{' '}<button type="button" onClick={() => decideApproval(member.id, 'Rejected')}>Reject</button>{' '}<button type="button" onClick={() => decideApproval(member.id, 'Correction requested')}>Request correction</button></td></tr>)}
            {pendingApprovals.length === 0 && <tr><td colSpan="5" className="empty">No pending registrations.</td></tr>}
          </tbody></table></div>
        </section>

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
                  <th>Posting</th>
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
                    <td>{user.posting || 'MEMBER'}</td>
                    <td>{memberIdDisplay(user) || '-'}{user.memberIdDisplayWarning && <small role="status">{user.memberIdDisplayWarning}</small>}</td>
                    <td>{new Date(user.createdAt).toLocaleDateString()}</td>
                    <td><span className="status-pill">{user.idCardStatus}</span></td>
                    <td><Link to={`/admin/users/${user.id}`}>View</Link></td>
                  </tr>
                ))}
                {users.length === 0 && (
                  <tr>
                    <td colSpan="8" className="empty">No users found.</td>
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
