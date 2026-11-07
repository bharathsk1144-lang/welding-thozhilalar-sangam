import { useState } from 'react';
import { Link } from 'react-router-dom';
import AppShell from '../src/components/AppShell.jsx';
import MembershipIdCard from '../src/components/MembershipIdCard.jsx';
import { normalizeDemoMobile, validateDemoIdentity, sampleCard } from './demoRegistration.js';

export default function DemoRegistration() {
  const [stage, setStage] = useState('identity');
  const [identity, setIdentity] = useState({ name: 'Demo Member', phone: '9876543210' });
  const [error, setError] = useState('');
  const [district, setDistrict] = useState('');
  const [acknowledged, setAcknowledged] = useState(false);
  function next(event) {
    event.preventDefault(); setError('');
    if (stage === 'identity') {
      const message = validateDemoIdentity(identity.name, identity.phone);
      if (message) return setError(message);
      setIdentity({ name: identity.name.trim(), phone: normalizeDemoMobile(identity.phone) });
      setStage('details');
    } else {
      if (!district || !acknowledged) return setError('Select a sample district and confirm the demo acknowledgement.');
      setIdentity({ name: '', phone: '' }); setStage('success');
    }
  }
  function reset() { setIdentity({ name: 'Demo Member', phone: '9876543210' }); setDistrict(''); setAcknowledged(false); setError(''); setStage('identity'); }
  return <AppShell><section className="demo-registration">
    <div className="demo-notice" role="note">Demo Mode — No actual registration will be saved. Use sample details only.</div>
    {stage === 'success' ? <>
      <div className="form-panel demo-success"><h1>Demo registration complete</h1><p>No account, login session or Member ID was issued. Your entries have been cleared. This sample card uses synthetic data only and is not valid for membership.</p><button className="primary" onClick={reset}>Try the demo again</button><Link to="/">Back to homepage</Link></div>
      <div className="demo-card"><strong className="demo-watermark">DEMO · SAMPLE ONLY · NOT VALID</strong><MembershipIdCard card={sampleCard} /></div>
    </> : <div className="form-panel"><p className="eyebrow">புதிய உறுப்பினர் · DEMO REGISTRATION</p>
      <h1>{stage === 'details' ? 'Step 2 — Sample member details' : 'Step 1 — Name and mobile'}</h1>
      <form onSubmit={next} noValidate>
        <label>Name / பெயர்<input name="name" maxLength="100" autoComplete="off" value={identity.name} readOnly={stage !== 'identity'} onChange={e => setIdentity({ ...identity, name: e.target.value })} /></label>
        <label>Mobile number / அலைபேசி<input name="phone" inputMode="tel" autoComplete="off" value={identity.phone} readOnly={stage !== 'identity'} onChange={e => setIdentity({ ...identity, phone: e.target.value })} /></label>
        {stage === 'details' && <>
          <p>Name and mobile are locked for this sample. All fields below are synthetic samples; no sensitive information is collected.</p>
          <div className="demo-fields">
            <label>Date of birth<input type="date" value="1990-01-01" readOnly /></label>
            <label>Father's name<input value="Sample Father" readOnly /></label>
            <label>District<select name="district" value={district} onChange={e => setDistrict(e.target.value)}><option value="">Select a sample district</option>{['Chennai', 'Madurai', 'Coimbatore', 'Salem', 'Tiruchirappalli'].map(value => <option key={value}>{value}</option>)}</select></label>
            <label>Blood group<input value="O+ (sample)" readOnly /></label>
            <label>House / permanent address<textarea value="Synthetic sample address — not a real home" readOnly /></label>
            <label>Location<input value="Sample location" readOnly /></label>
            <label>Pincode<input value="600001" readOnly /></label>
          </div>
          <p className="demo-private-fields">Passport Size Photo and Aadhaar document: synthetic placeholders only. Password entry and file uploads are disabled in this public demo.</p>
          <label className="demo-ack"><input type="checkbox" checked={acknowledged} onChange={e => setAcknowledged(e.target.checked)} /> I understand this is a sample registration and no information will be saved.</label>
        </>}
        {error && <p role="alert" className="error-message">{error}</p>}
        <button type="submit" className="primary">{stage === 'identity' ? 'Continue' : 'Complete demo registration'}</button>
      </form>
    </div>}
  </section></AppShell>;
}
