import { branding } from '../config/branding.js';
import { useEffect, useMemo, useRef, useState } from 'react';
import { Link } from 'react-router-dom';
import AppShell from '../components/AppShell.jsx';
import TamilDocument from '../components/TamilDocument.jsx';
import registrationTerms from '../content/registration-terms-tamil.md?raw';
import { api } from '../api/client.js';
import { normalizeMobile, MOBILE_PATTERN } from '../api/mobile.js';
import { dobToIso, formatDobInput, isoToDob } from '../api/dob.js';
import { clearRegistrationDraft, readRegistrationDraft, saveRegistrationDraft } from '../api/registrationDraft.js';
import { inspectImage } from '../api/imageSuitability.js';
import PhotoCropper from '../components/PhotoCropper.jsx';
import { validAadhaarNumber, AADHAAR_HINT } from '../api/aadhaarNumber.js';

const emptyForm = {
  name: '',
  dateOfBirth: '',
  bloodGroup: '',
  fatherName: '',
  permanentAddress: '',
  pincode: '',
  district: '',
  aadhaarNumber: '',
  profilePhoto: null,
  hasJoinedWhatsAppGroup: false,
  consentAccepted: false
};

export default function RegistrationPage() {
  const restored = useRef(readRegistrationDraft());
  const dobPicker = useRef(null);
  const submitting = useRef(false);
  const imageSelection = useRef({});
  const draftCancelled = useRef(false);
  const passportInput = useRef(null);
  const [imageWarnings, setImageWarnings] = useState({});
  const [imagePreviews, setImagePreviews] = useState({});
  const activePreviews = useRef({});
  const [cropping, setCropping] = useState(false);
  useEffect(() => {
    for (const [name, url] of Object.entries(activePreviews.current)) {
      if (imagePreviews[name] !== url) URL.revokeObjectURL(url);
    }
    activePreviews.current = imagePreviews;
  }, [imagePreviews]);
  useEffect(() => () => Object.values(activePreviews.current).forEach((url) => URL.revokeObjectURL(url)), []);
  const [districts, setDistricts] = useState([]);
  const [registrationOtpRequired, setRegistrationOtpRequired] = useState(null);
  const [step, setStep] = useState('identity');
  const [identity, setIdentity] = useState({
    name: restored.current?.name || '',
    phoneNumber: restored.current?.phoneNumber || '',
    password: '',
    confirmPassword: ''
  });
  const [otp, setOtp] = useState('');
  const [verificationToken, setVerificationToken] = useState('');
  const [form, setForm] = useState(() => ({ ...emptyForm, ...restored.current?.fields }));
  const [message, setMessage] = useState(restored.current ? 'Your registration details have been restored.' : '');
  const [error, setError] = useState('');
  const [loading, setLoading] = useState(false);
  const [duplicateMobile, setDuplicateMobile] = useState(false);

  useEffect(() => {
    api.getDistricts().then(setDistricts).catch((err) => setError(err.message));
    api.getRegistrationOptions().then((options) => {
      if (typeof options.otpEnabled !== 'boolean') throw new Error('Registration settings are unavailable.');
      setRegistrationOtpRequired(options.otpEnabled);
    })
      .catch((err) => setError(err.message));
  }, []);

  useEffect(() => {
    if (step === 'success' || draftCancelled.current) return;
    if (!identity.name && !identity.phoneNumber && !form.permanentAddress && !form.district) return;
    const save = () => saveRegistrationDraft(identity, form);
    const onResume = () => { if (document.visibilityState === 'visible' && readRegistrationDraft()) save(); };
    save();
    window.addEventListener('pagehide', save);
    document.addEventListener('visibilitychange', onResume);
    return () => { window.removeEventListener('pagehide', save); document.removeEventListener('visibilitychange', onResume); };
  }, [identity.name, identity.phoneNumber, form, step]);

  const selectedDistrict = useMemo(
    () => districts.find((district) => district.name === form.district),
    [districts, form.district]
  );

  async function handleSendOtp(event) {
    event.preventDefault();
    setError('');
    setMessage('');

    if (!identity.name.trim() || identity.name.trim().length > 100) {
      setError('Enter a name of up to 100 characters.');
      return;
    }

    const phone = normalizeMobile(identity.phoneNumber);
    if (!phone) {
      setError('Enter a valid Indian mobile number, optionally prefixed by +91 or 91.');
      return;
    }

    if (!registrationOtpRequired) {
      setIdentity((current) => ({ ...current, name: identity.name.trim(), phoneNumber: phone }));
      setDuplicateMobile(false);
      setForm((current) => ({ ...current, name: identity.name.trim() }));
      setStep('form');
      return;
    }

    setLoading(true);
    try {
      const result = await api.sendOtp({ name: identity.name.trim(), phoneNumber: phone });
      setIdentity((current) => ({ ...current, name: identity.name.trim(), phoneNumber: phone }));
      setDuplicateMobile(false);
      setForm((current) => ({ ...current, name: identity.name.trim() }));
      setStep('otp');
      setMessage(result.developmentOtp ? `OTP request successful. Demo OTP is ${result.developmentOtp}.` : 'OTP sent successfully.');
    } catch (err) {
      setError(err.message);
      if (err.code === 'mobile_already_registered') {
        setDuplicateMobile(true);
        setVerificationToken('');
        setStep('identity');
      }
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
      const result = await api.verifyOtp({ phoneNumber: identity.phoneNumber.trim(), otp: otp.trim() });
      setVerificationToken(result.verificationToken);
      setForm((current) => ({ ...current, name: current.name || identity.name.trim() }));
      setStep('form');
      setMessage('OTP verified. Complete your registration.');
    } catch (err) {
      setError(err.message);
      if (err.code === 'mobile_already_registered') {
        setDuplicateMobile(true); setVerificationToken(''); setStep('identity');
      }
    } finally {
      setLoading(false);
    }
  }

  function updateForm(event) {
    draftCancelled.current = false;
    const { name, value, type, checked, files } = event.target;
    if (type === 'file') { chooseImage(name, files[0]); return; }
    if (name === 'aadhaarNumber' && !/^[0-9]{0,12}$/.test(value)) { setError(AADHAAR_HINT); return; }
    setForm((current) => ({
      ...current,
      [name]: type === 'checkbox' ? checked : type === 'file' ? files[0] : name === 'dateOfBirth' ? formatDobInput(value) : value
    }));
  }

  async function chooseImage(name, file) {
    draftCancelled.current = false;
    if (!file) return;
    const selection = Symbol(name);
    imageSelection.current[name] = selection;
    setForm((current) => ({ ...current, [name]: null }));
    setImagePreviews((current) => ({ ...current, [name]: '' }));
    setImageWarnings((current) => ({ ...current, [name]: 'Checking image locally…' }));
    setError('');
    let result;
    try { result = await inspectImage(file, 'passport'); }
    catch { result = { error: 'This image could not be checked. Choose another image.' }; }
    if (imageSelection.current[name] !== selection) return;
    setImageWarnings((current) => ({ ...current, [name]: result.error || result.warning || '' }));
    if (result.error) { setError(result.error); return; }
    setForm((current) => ({ ...current, [name]: file }));
    const preview = URL.createObjectURL(file);
    setImagePreviews((current) => ({ ...current, [name]: preview }));
  }

  function validateFile(file, maxMb, label) {
    if (!file) return `${label} is required.`;
    if (file.size > maxMb * 1024 * 1024) return `${label} must be ${maxMb} MB or less.`;
    if (!['image/jpeg', 'image/png', 'image/webp'].includes(file.type)) {
      return `${label} must be JPG, PNG, or WEBP.`;
    }
    return '';
  }

  async function handleSubmit(event) {
    event.preventDefault();
    if (submitting.current) return;
    setError('');
    setMessage('');

    if (registrationOtpRequired && !verificationToken) {
      setError('Please start again and verify OTP before submitting registration.');
      setStep('identity');
      return;
    }

    if (identity.password.length < 8) {
      setError('Password must be at least 8 characters.');
      return;
    }

    if (identity.password !== identity.confirmPassword) {
      setError('Confirm password must match password.');
      return;
    }

    const isoDob = dobToIso(form.dateOfBirth);
    if (!isoDob) { setError('Enter a valid past date in DD-MM-YYYY / சரியான பிறந்த தேதியை உள்ளிடவும்.'); return; }

    if (!form.hasJoinedWhatsAppGroup) {
      setError('Kindly join the WhatsApp group before submitting.');
      return;
    }

    if (!form.consentAccepted) {
      setError('மேலே உள்ள சங்கத்தின் விதிமுறைகளைப் படித்து ஒப்புக்கொள்ளவும்.');
      return;
    }

    if (!validAadhaarNumber(form.aadhaarNumber)) { setError(AADHAAR_HINT); return; }
    const profileError = validateFile(form.profilePhoto, 3, 'Passport Size Photo');
    if (profileError) {
      setError(profileError);
      return;
    }
    submitting.current = true;
    for (const [name, kind] of [['profilePhoto', 'passport']]) {
      let result;
      try { result = await inspectImage(form[name], kind); }
      catch { result = { error: 'This image could not be checked. Choose another image.' }; }
      if (result.error) { setError(result.error); submitting.current = false; return; }
    }

    const formData = new FormData();
    if (registrationOtpRequired) formData.append('verificationToken', verificationToken);
    formData.append('username', identity.phoneNumber.trim());
    formData.append('phoneNumber', identity.phoneNumber.trim());
    formData.append('password', identity.password);
    Object.entries(form).forEach(([key, value]) => {
      if (value !== null) formData.append(key, key === 'name' ? identity.name : key === 'dateOfBirth' ? isoDob : value);
    });

    setLoading(true);
    try {
      const result = await api.registerUser(formData);
      clearRegistrationDraft();
      setForm((current) => ({ ...current, aadhaarNumber: '' }));
      setMessage(result.message);
      if (result.userId) {
        sessionStorage.setItem('wtsUserToken', result.token);
        localStorage.setItem('wtsUser', JSON.stringify({
          userId: result.userId,
          phone: identity.phoneNumber.trim(),
          approvalStatus: 'Pending'
        }));
      }
      setStep('success');
    } catch (err) {
      setError(err.message);
      if (err.code === 'mobile_already_registered') {
        setDuplicateMobile(true);
        setVerificationToken('');
        setStep('identity');
      }
    } finally {
      submitting.current = false;
      setLoading(false);
    }
  }

  return (
    <AppShell>
      <section className="workspace">
        <aside className="panel intro-panel">
          <img src={branding.logo} alt="" className="intro-logo" />
          <p className="eyebrow">New User Registration</p>
          <h1>Member registration for welding workers</h1>
          <p>
            Enter your name and mobile number, select your Tamil Nadu district, join the assigned WhatsApp group,
            and submit your documents securely.
          </p>
          <div className="intro-list">
            <span>{registrationOtpRequired === null ? 'Loading registration settings' : registrationOtpRequired ? 'Name + mobile OTP verification' : 'Name + mobile registration'}</span>
            <span>Tamil + English labels</span>
            <span>District-wise member records</span>
          </div>
        </aside>

        <div className="panel form-panel">
          {message && <div className="notice success" role="status">{message}</div>}
          {error && <div className="notice error" role="alert">{error}</div>}
          {duplicateMobile && (
            <div className="id-actions">
              <Link to="/user-login" className="button-link primary">Go to User Login</Link>
              <Link to="/forgot-password" className="button-link secondary">Forgot Password</Link>
            </div>
          )}

          {step === 'identity' && registrationOtpRequired !== null && (
            <form onSubmit={handleSendOtp} className="form-grid">
              <div className="form-heading">
                <p className="eyebrow">Step 1</p>
                <h1>Start registration</h1>
                <small>{registrationOtpRequired ? 'OTP is required only once to verify your mobile number.' : 'Enter your name and mobile number to continue.'}</small>
              </div>
              <div className="form-section single">
                <label>
                  <span>Name / பெயர்</span>
                  <input
                    required
                    autoComplete="name"
                    maxLength={100}
                    value={identity.name}
                    onChange={(event) => {
                      const name = event.target.value;
                      draftCancelled.current = false; setIdentity({ ...identity, name });
                      setForm((current) => ({ ...current, name }));
                    }}
                    placeholder="Enter member name"
                    title="Enter member name"
                  />
                </label>
                <label>
                  <span>Mobile Number / தொலைபேசி எண்</span>
                  <input
                    required
                    inputMode="numeric"
                    autoComplete="tel"
                    value={identity.phoneNumber}
                    onChange={(event) => {
                      setDuplicateMobile(false);
                      setError('');
                      draftCancelled.current = false; setIdentity({ ...identity, phoneNumber: event.target.value });
                    }}
                    placeholder="10 digit mobile number"
                    pattern={MOBILE_PATTERN}
                    title="Enter a valid 10 digit mobile number"
                  />
                  <small>Enter an Indian mobile number; +91 or 91 prefix is accepted.</small>
                </label>
              </div>
              <button disabled={loading || duplicateMobile} className="primary">{registrationOtpRequired ? (loading ? 'Sending OTP...' : 'Send OTP') : 'Continue'}</button>
            </form>
          )}

          {registrationOtpRequired && step === 'otp' && (
            <form onSubmit={handleVerifyOtp} className="form-grid">
              <div className="form-heading">
                <p className="eyebrow">Step 1 · Mobile verification</p>
                <h1>Verify OTP</h1>
                <small>OTP சரிபார்ப்பு - enter the code sent for registration.</small>
              </div>
              <div className="form-section single">
                <label>
                  <span>OTP / ஒருமுறை கடவுச்சொல்</span>
                  <input
                    required
                    inputMode="numeric"
                    autoComplete="one-time-code"
                    maxLength={6}
                    value={otp}
                    onChange={(event) => setOtp(event.target.value)}
                    placeholder="6 digit OTP"
                    pattern="[0-9]{6}"
                    title="Enter the 6 digit OTP"
                  />
                </label>
              </div>
              <button disabled={loading} className="primary">{loading ? 'Verifying...' : 'Verify OTP'}</button>
            </form>
          )}

          {(step === 'otp' || step === 'form') && (
            <button type="button" className="text-button" disabled={loading} onClick={() => {
              draftCancelled.current = true; setVerificationToken(''); setOtp(''); setStep('identity'); setForm(emptyForm); clearRegistrationDraft();
              setIdentity((current) => ({ ...current, password: '', confirmPassword: '' }));
              setMessage(''); setError('');
            }}>Change name or mobile · Start again</button>
          )}
          {step === 'form' && (
            <form onSubmit={handleSubmit} className="registration-form">
              <div className="form-heading">
                <p className="eyebrow">Step 2 · Membership details</p>
                <h1>Complete your details</h1>
                <small>உறுப்பினர் விவரங்கள் - name and mobile number are locked after continuing.</small>
              </div>

              <div className="form-section">
                <div className="section-kicker">Set your login password</div>
                <p className="form-help">Use your mobile number and this password to access your ID card later.</p>
                <div className="form-grid two">
                <label>
                  <span>Password</span>
                  <input
                    required
                    type="password" autoComplete="new-password"
                    value={identity.password}
                    onChange={(event) => setIdentity({ ...identity, password: event.target.value })}
                    placeholder="Minimum 8 characters"
                    minLength={8}
                    title="Password must be at least 8 characters"
                  />
                </label>
                <label>
                  <span>Confirm Password</span>
                  <input
                    required
                    type="password" autoComplete="new-password"
                    value={identity.confirmPassword}
                    onChange={(event) => setIdentity({ ...identity, confirmPassword: event.target.value })}
                    placeholder="Re-enter password"
                    minLength={8}
                    title="Confirm password must match"
                  />
                </label>
                </div>
              </div>
              <div className="form-section">
                <div className="section-kicker">Personal details</div>
                <div className="form-grid two">
                  <label>
                    <span>Full Name / முழு பெயர்</span>
                    <input readOnly name="name" value={identity.name} title="Name used for mobile verification" />
                    <small>Locked after verification. Start again to change your name.</small>
                  </label>
                  <label>
                    <span>Mobile Number / தொலைபேசி எண்</span>
                    <input readOnly inputMode="numeric" value={identity.phoneNumber} title="Verified mobile number" />
                    <small>Verified in Step 1. Start again to change this number.</small>
                  </label>
                  <label>
                    <span>Date of Birth / பிறந்த தேதி</span>
                    <input required inputMode="numeric" autoComplete="bday" name="dateOfBirth" value={form.dateOfBirth} onChange={updateForm} placeholder="DD-MM-YYYY" maxLength={10} pattern="[0-9]{2}-[0-9]{2}-[0-9]{4}" />
                    <small>DD-MM-YYYY · நாள்-மாதம்-ஆண்டு. Type the date or use the picker.</small>
                    <input ref={dobPicker} type="date" aria-label="Choose date of birth" value={dobToIso(form.dateOfBirth) || ''} max={new Date().toISOString().slice(0, 10)} onChange={(event) => setForm((current) => ({ ...current, dateOfBirth: isoToDob(event.target.value) }))} />
                  </label>
                  <label>
                    <span>Blood Group / இரத்த வகை</span>
                    <select required name="bloodGroup" value={form.bloodGroup} onChange={updateForm}>
                      <option value="">Select blood group</option>
                      {['A+', 'A-', 'B+', 'B-', 'AB+', 'AB-', 'O+', 'O-'].map((group) => (
                        <option key={group} value={group}>{group}</option>
                      ))}
                    </select>
                  </label>
                  <label>
                    <span>Father Name / தந்தை பெயர்</span>
                    <input required name="fatherName" value={form.fatherName} onChange={updateForm} placeholder="Father name" />
                  </label>
                </div>
              </div>

              <div className="form-section">
                <div className="section-kicker">Address and district</div>
                <div className="form-grid two">
                  <label className="wide">
                    <span>முழு முகவரி மற்றும் அஞ்சல் குறியீடு / Full Address with Pincode</span>
                    <textarea
                      required
                      name="permanentAddress"
                      value={form.permanentAddress}
                      onChange={updateForm}
                      placeholder="வீட்டு எண், தெரு, ஊர் / Door number, street, village or town"
                    />
                    <small>முழு வீட்டு முகவரியை எழுதவும். கீழே உள்ள தனி புலத்தில் 6 இலக்க அஞ்சல் குறியீட்டை உள்ளிடவும். / Enter your full residential address and its 6-digit pincode in the separate field below.</small>
                  </label>
                  <label>
                    <span>Pincode / அஞ்சல் குறியீடு</span>
                    <input
                      required
                      inputMode="numeric"
                      name="pincode"
                      maxLength={6}
                      value={form.pincode}
                      onChange={updateForm}
                      pattern="[0-9]{6}"
                      title="Enter a valid 6 digit pincode"
                      placeholder="6 digit pincode"
                    />
                  </label>
                  <label>
                    <span>District / மாவட்டம்</span>
                    <select required name="district" value={form.district} onChange={updateForm}>
                      <option value="">Select district</option>
                      {districts.map((district) => (
                        <option key={district.id} value={district.name}>
                          {district.name}
                        </option>
                      ))}
                    </select>
                  </label>
                </div>
              </div>

              {selectedDistrict && (
                <div className="whatsapp-box">
                  <div>
                    <span>District WhatsApp Group / மாவட்ட WhatsApp குழு</span>
                    <small>Join your district group before submitting the registration.</small>
                  </div>
                  <a href={selectedDistrict.whatsAppGroupLink} target="_blank" rel="noreferrer">
                    Join {selectedDistrict.name} group
                  </a>
                  <label className="check-row">
                    <input
                      type="checkbox"
                      name="hasJoinedWhatsAppGroup"
                      checked={form.hasJoinedWhatsAppGroup}
                      onChange={updateForm}
                    />
                    <span>I have joined the WhatsApp group / நான் WhatsApp குழுவில் சேர்ந்துவிட்டேன்</span>
                  </label>
                </div>
              )}

              <div className="form-section">
                <div className="section-kicker">Documents</div>
                <div className="form-grid two">
                  <label>
                    <span>ஆதார் எண் / Aadhaar Number <strong className="required-star" aria-label="required">*</strong></span>
                    <input required type="text" name="aadhaarNumber" inputMode="numeric" autoComplete="off" pattern="[2-9][0-9]{11}" maxLength={12} value={form.aadhaarNumber} onChange={updateForm} aria-describedby="aadhaar-number-hint" />
                    <small id="aadhaar-number-hint">{AADHAAR_HINT}</small>
                  </label>
                  <label>
                    <span>Passport Size Photo / பாஸ்போர்ட் அளவு படம்</span>
                    <strong className="upload-warning">PLEASE UPLOAD ONLY PASSPORT SIZE PHOTO WITH A CLEAR BACKGROUND.</strong>
                    <input ref={passportInput} required={!form.profilePhoto} type="file" name="profilePhoto" accept=".jpg,.jpeg,.png,.webp,image/jpeg,image/png,image/webp" onChange={updateForm} />
                    {imagePreviews.profilePhoto && <img className="upload-preview" src={imagePreviews.profilePhoto} alt="Selected passport preview" />}
                    {imageWarnings.profilePhoto && <small role="status">{imageWarnings.profilePhoto}</small>}
                    <button type="button" className="secondary" onClick={() => passportInput.current?.click()}>Choose Another Photo</button>
                    {form.profilePhoto && <button type="button" className="secondary" onClick={() => setCropping(true)}>Crop Photo</button>}
                    <small>Upload clear passport size photo. Max 3 MB.</small>
                  </label>
                </div>
                {cropping && form.profilePhoto && <PhotoCropper file={form.profilePhoto} onSave={async (cropped) => { await chooseImage('profilePhoto', cropped); setCropping(false); }} onCancel={() => setCropping(false)} />}
              </div>

              <div className="form-section consent-box">
                <TamilDocument source={registrationTerms} />
                <label className="check-row">
                  <input
                    required
                    type="checkbox"
                    name="consentAccepted"
                    checked={form.consentAccepted}
                    onChange={updateForm}
                  />
                  <span>
                    மேலே உள்ள சங்கத்தின் விதிமுறைகளைப் படித்து புரிந்துகொண்டு ஒப்புக்கொள்கிறேன். அளித்த விவரங்களும் ஆவணங்களும் சரியானவை என்பதை உறுதிப்படுத்துகிறேன்.
                  </span>
                </label>
              </div>

              <div
                className="submit-guard"
                onClick={() => {
                  if (!form.hasJoinedWhatsAppGroup) {
                    setError('Kindly join the WhatsApp group before submitting.');
                  } else if (!form.consentAccepted) {
                    setError('மேலே உள்ள சங்கத்தின் விதிமுறைகளைப் படித்து ஒப்புக்கொள்ளவும்.');
                  }
                }}
              >
                <button disabled={loading || !form.hasJoinedWhatsAppGroup || !form.consentAccepted} className="primary">
                  {loading ? 'Submitting...' : 'Submit Registration'}
                </button>
              </div>
            </form>
          )}

          {step === 'success' && (
            <div className="done-card">
              <img src={branding.logo} alt="" className="done-logo" />
              <span className="done-icon">OK</span>
              <h1>உங்கள் பதிவு வெற்றிகரமாகச் சமர்ப்பிக்கப்பட்டது!</h1>
              <p>உங்கள் உறுப்பினர் பதிவு தற்போது நிர்வாகக் குழுவின் பரிசீலனையில் உள்ளது. நிர்வாக ஒப்புதல் வழங்கப்பட்டவுடன், உங்கள் உறுப்பினர் அடையாள அட்டையைப் பார்க்கவும் பதிவிறக்கம் செய்யவும் முடியும்.</p>
              <small>தயவுசெய்து ஒப்புதல் கிடைக்கும் வரை காத்திருக்கவும். நன்றி!</small>
              <Link to="/" className="button-link secondary">Back to Home</Link>
            </div>
          )}
        </div>
      </section>
    </AppShell>
  );
}
