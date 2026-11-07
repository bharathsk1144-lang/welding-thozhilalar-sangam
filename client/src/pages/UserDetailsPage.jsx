import { useEffect, useRef, useState } from 'react';
import { Link, useParams } from 'react-router-dom';
import AppShell from '../components/AppShell.jsx';
import MembershipIdCard, { downloadIdCard } from '../components/MembershipIdCard.jsx';
import { api } from '../api/client.js';
import { memberIdDisplay } from '../api/memberId.js';
import ProtectedImage from '../components/ProtectedImage.jsx';

export default function UserDetailsPage() {
  const { id } = useParams();
  const cardRef = useRef(null);
  const [user, setUser] = useState(null);
  const [card, setCard] = useState(null);
  const [error, setError] = useState('');
  const [downloadMessage, setDownloadMessage] = useState('');
  const [downloadError, setDownloadError] = useState('');
  const [downloading, setDownloading] = useState(false);

  useEffect(() => {
    api.adminUserDetails(id).then(setUser).catch((err) => setError(err.message));
    api.idCard(id).then(setCard).catch(() => {});
  }, [id]);

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

  return (
    <AppShell admin>
      <section className="details-wrap">
        {downloadError && <div className="notice error" role="alert">{downloadError}</div>}
        <Link to="/admin/dashboard" className="back-link">Back to dashboard</Link>
        {error && <div className="notice error">{error}</div>}
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
              <Detail label="Verification Status" value={user.verificationStatus || 'Verified'} />
              <Detail label="Consent Accepted" value={user.consentAccepted ? 'Yes' : 'No'} />
              <Detail label="Member ID" value={memberIdDisplay(user) || 'Awaiting assignment'} />
              {memberIdDisplay(user) !== user.memberId && <Detail label="Original printed Member ID" value={user.memberId} />}
              <Detail label="Permanent Address / நிரந்தர முகவரி" value={user.permanentAddress} wide />
            </div>

            {user.memberIdDisplayWarning && <div className="notice error" role="status">{user.memberIdDisplayWarning}</div>}
            <div className="detail-actions">
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
