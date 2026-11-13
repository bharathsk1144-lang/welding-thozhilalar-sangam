import { useEffect, useRef, useState } from 'react';
import { Link, useParams } from 'react-router-dom';
import AppShell from '../components/AppShell.jsx';
import MembershipIdCard, { downloadIdCard } from '../components/MembershipIdCard.jsx';
import { api } from '../api/client.js';
import { memberIdDisplay } from '../api/memberId.js';
import ProtectedImage from '../components/ProtectedImage.jsx';
import { normalizeMobile, MOBILE_PATTERN } from '../api/mobile.js';

const editableFields = ['name', 'phoneNumber', 'location', 'dateOfBirth', 'bloodGroup', 'fatherName', 'permanentAddress', 'pincode', 'district'];
const editValues = (member) => Object.fromEntries(editableFields.map((field) => [field,
  field === 'dateOfBirth' ? (member[field] || '').slice(0, 10) : member[field] || ''
]));

export default function UserDetailsPage() {
  const { id } = useParams();
  const cardRef = useRef(null);
  const [user, setUser] = useState(null);
  const [card, setCard] = useState(null);
  const [error, setError] = useState('');
  const [downloadMessage, setDownloadMessage] = useState('');
  const [downloadError, setDownloadError] = useState('');
  const [downloading, setDownloading] = useState(false);
  const [editing, setEditing] = useState(false);
  const [form, setForm] = useState(null);
  const [districts, setDistricts] = useState([]);
  const [saving, setSaving] = useState(false);
  const [message, setMessage] = useState('');

  useEffect(() => {
    api.adminUserDetails(id).then(setUser).catch((err) => setError(err.message));
    api.idCard(id).then(setCard).catch(() => {});
  }, [id]);

  async function startEditing() {
    setError(''); setMessage('');
    try {
      setDistricts(await api.getDistricts());
      setForm(editValues(user));
      setEditing(true);
    } catch (err) { setError(err.message); }
  }

  async function saveChanges(event) {
    event.preventDefault();
    setError(''); setMessage('');
    const phone = normalizeMobile(form.phoneNumber);
    if (!phone) { setError('Enter a valid Indian mobile number.'); return; }
    if (!/^\d{6}$/.test(form.pincode.trim())) { setError('Pincode must be 6 digits.'); return; }
    if (!form.dateOfBirth || form.dateOfBirth >= new Date().toISOString().slice(0, 10)) {
      setError('A valid date of birth is required.'); return;
    }
    setSaving(true);
    let saved = false;
    try {
      await api.adminUpdateUser(id, { ...form, phoneNumber: phone });
      saved = true;
      setEditing(false); setForm(null); setCard(null);
      const [updated, updatedCard] = await Promise.all([api.adminUserDetails(id), api.idCard(id)]);
      setUser(updated); setCard(updatedCard);
      setMessage('Member details saved successfully. The ID card preview is updated.');
    } catch (err) { setError(saved ? `Changes were saved, but refresh failed. Reload this page. ${err.message}` : err.message); }
    finally { setSaving(false); }
  }

  async function handleDownload() {
    setDownloading(true);
    setDownloadMessage('');
    setDownloadError('');
    setError('');
    try {
      const result = await downloadIdCard(cardRef.current, memberIdDisplay(card || user));
      setDownloadMessage(
        result?.type === 'png'
          ? 'PDF download failed. A PNG with both sides was downloaded instead.'
          : 'Both sides of your ID card downloaded successfully.'
      );
    } catch (err) {
      setDownloadError(`ID card download failed. Please try again. ${err.message || ''}`.trim());
    } finally {
      setDownloading(false);
    }
  }

  async function setApproval(approved) {
    setError('');
    try {
      await api.setMemberApproval(id, approved);
      const [updatedUser, updatedCard] = await Promise.all([api.adminUserDetails(id), api.idCard(id)]);
      setUser(updatedUser);
      setCard(updatedCard);
    } catch (err) { setError(err.message || 'Unable to update member approval.'); }
  }

  return (
    <AppShell admin>
      <section className="details-wrap">
        {downloadError && <div className="notice error" role="alert">{downloadError}</div>}
        <Link to="/admin/dashboard" className="back-link">Back to dashboard</Link>
        {error && <div className="notice error">{error}</div>}
        {message && <div className="notice success" role="status">{message}</div>}
        {downloadMessage && <div className="notice success">{downloadMessage}</div>}
        {user && (
          <div className="panel details-panel">
            <div className="section-title">
              <div>
                <p className="eyebrow">Member details</p>
                <h1>{user.name}</h1>
              </div>
              <span className="district-pill">{user.district}</span>
            </div>

            {editing && form && (
              <form className="registration-form" onSubmit={saveChanges}>
                <div className="form-heading"><h2>Edit member details</h2><small>Member ID, dates, approval, consent, documents, and signatures are preserved.</small></div>
                <div className="form-grid two">
                  <label><span>Name</span><input required maxLength={100} name="name" value={form.name} onChange={(event) => setForm({ ...form, name: event.target.value })} /></label>
                  <label><span>Mobile Number</span><input required name="phoneNumber" inputMode="numeric" pattern={MOBILE_PATTERN} value={form.phoneNumber} onChange={(event) => setForm({ ...form, phoneNumber: event.target.value })} /></label>
                  <label><span>Location / Posting</span><input required maxLength={150} name="location" value={form.location} onChange={(event) => setForm({ ...form, location: event.target.value })} /></label>
                  <label><span>Date of Birth</span><input required type="date" name="dateOfBirth" value={form.dateOfBirth} onChange={(event) => setForm({ ...form, dateOfBirth: event.target.value })} /></label>
                  <label><span>Blood Group</span><select required name="bloodGroup" value={form.bloodGroup} onChange={(event) => setForm({ ...form, bloodGroup: event.target.value })}><option value="">Select blood group</option>{['A+', 'A-', 'B+', 'B-', 'AB+', 'AB-', 'O+', 'O-'].map((group) => <option key={group} value={group}>{group}</option>)}</select></label>
                  <label><span>Father Name</span><input required maxLength={100} name="fatherName" value={form.fatherName} onChange={(event) => setForm({ ...form, fatherName: event.target.value })} /></label>
                  <label className="wide"><span>Permanent Address</span><textarea required maxLength={500} name="permanentAddress" value={form.permanentAddress} onChange={(event) => setForm({ ...form, permanentAddress: event.target.value })} /></label>
                  <label><span>Pincode</span><input required name="pincode" inputMode="numeric" pattern="[0-9]{6}" value={form.pincode} onChange={(event) => setForm({ ...form, pincode: event.target.value })} /></label>
                  <label><span>District</span><select required name="district" value={form.district} onChange={(event) => setForm({ ...form, district: event.target.value })}><option value="">Select district</option>{districts.map((district) => <option key={district.id} value={district.name}>{district.name}</option>)}</select></label>
                </div>
                <div className="detail-actions"><button className="primary" disabled={saving}>{saving ? 'Saving...' : 'Save Changes'}</button><button type="button" className="secondary" disabled={saving} onClick={() => { setEditing(false); setForm(null); setError(''); }}>Cancel</button></div>
              </form>
            )}

            <div className="details-grid">
              <Detail label="Username / பயனர் பெயர்" value={user.username} />
              <Detail label="Phone Number / தொலைபேசி எண்" value={user.phoneNumber} />
              <Detail label="Location / இடம்" value={user.location} />
              <Detail label="Date of Birth / பிறந்த தேதி" value={new Date(user.dateOfBirth).toLocaleDateString()} />
              <Detail label="Blood Group / இரத்த வகை" value={user.bloodGroup} />
              <Detail label="Father Name / தந்தை பெயர்" value={user.fatherName} />
              <Detail label="Pincode / அஞ்சல் குறியீடு" value={user.pincode} />
              <Detail label="ID Card Status" value={user.idCardStatus} />
              <Detail label="Status" value={user.status || 'Active Member'} />
              <Detail label="Verification Status" value={user.verificationStatus || 'Pending'} />
              <Detail label="Approved for VERIFIED badge" value={card?.isApproved ? 'Yes' : 'No'} />
              <Detail label="Consent Accepted" value={user.consentAccepted ? 'Yes' : 'No'} />
              <Detail label="Member ID" value={memberIdDisplay(user) || 'Awaiting assignment'} />
              {memberIdDisplay(user) !== user.memberId && <Detail label="Original printed Member ID" value={user.memberId} />}
              <Detail label="Permanent Address / நிரந்தர முகவரி" value={user.permanentAddress} wide />
            </div>

            {user.memberIdDisplayWarning && <div className="notice error" role="status">{user.memberIdDisplayWarning}</div>}
            <div className="detail-actions">
              {!editing && <button className="secondary" type="button" onClick={startEditing}>Edit</button>}
              <button className="secondary" type="button" onClick={() => setApproval(!card?.isApproved)}>{card?.isApproved ? 'Revoke approval' : 'Approve member'}</button>
              <Link to={`/id-card/${user.id}`} className="button-link primary">View ID Card</Link>
              <button className="secondary" disabled={!card || downloading} onClick={handleDownload}>
                {downloading ? 'Preparing Download...' : 'Download ID Card'}
              </button>
            </div>

            <div className="photo-grid">
              <figure>
                <ProtectedImage path={user.aadhaarPhotoPath} ownerId={user.id} adminOnly alt="Aadhaar upload" />
                <figcaption>Aadhaar card photo</figcaption>
              </figure>
              <figure>
                <ProtectedImage path={user.profilePhotoPath} ownerId={user.id} adminOnly alt="Passport size upload" />
                <figcaption>Passport Size Photo</figcaption>
              </figure>
            </div>

            {card && (
              <div className="admin-id-preview">
                <div className="section-title">
                  <div>
                    <p className="eyebrow">ID Card Preview</p>
                    <h2>Membership card</h2>
                  </div>
                </div>
                <MembershipIdCard ref={cardRef} card={card} />
              </div>
            )}
          </div>
        )}
      </section>
    </AppShell>
  );
}

function Detail({ label, value, wide = false }) {
  return (
    <div className={wide ? 'detail wide' : 'detail'}>
      <span>{label}</span>
      <strong>{value || '-'}</strong>
    </div>
  );
}
