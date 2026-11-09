import { forwardRef } from 'react';
import ProtectedImage from './ProtectedImage.jsx';
import ProtectedOfficerSignature from './ProtectedOfficerSignature.jsx';
import OrganizationContactIcon from './OrganizationContactIcon.jsx';
import { branding, officeAddressText, organizationContactRows } from '../config/branding.js';
import { memberIdDisplay, memberIdFilePart } from '../api/memberId.js';

const text = (value, missing = 'Not recorded') => String(value ?? '').trim() || missing;
function date(value) {
  const parsed = value ? new Date(value) : null;
  return !parsed || Number.isNaN(parsed.getTime()) || parsed.getFullYear() < 1900 ? 'Not recorded'
    : parsed.toLocaleDateString('en-IN', { day: '2-digit', month: 'short', year: 'numeric' });
}
function Field({ label, value, className = '' }) {
  return <div className={`pass-field ${className}`}><dt>{label}</dt><dd>{text(value)}</dd></div>;
}
function CardBrand() {
  return <header className="pass-brand">
    <div className="pass-brand__identity">
      <img src={branding.logo} alt="Sangam crest" width="64" height="64" />
      <div className="pass-brand__names"><span>{branding.nameEnglish}</span><strong lang="ta">{branding.nameTamil}</strong></div>
    </div>
    <section className="pass-office" aria-label="Organization office address"><p>{officeAddressText().split(/\r?\n/).map((line) => <span key={line}>{line}</span>)}</p>{branding.phone && <span>{branding.phone}</span>}{branding.email && <span>{branding.email}</span>}</section>
  </header>;
}

const MembershipIdCard = forwardRef(function MembershipIdCard({ card = {} }, ref) {
  const displayId = memberIdDisplay(card);
  return <div className="card-preview">
    <div className="card-preview__heading"><p className="eyebrow">YOUR MEMBERSHIP · BOTH SIDES</p><h2 lang="ta">உறுப்பினர் அடையாள அட்டை</h2><p>Preview the front and back. Your PDF includes one side per page.</p></div>
    <div ref={ref} className="membership-card card-pair" aria-label="Membership ID card front and back">
      <article className="member-pass member-pass--front" data-card-side="front" aria-label="Front of membership card">
        <span className="pass-side-label">FRONT</span>
        <CardBrand />
        <div className="pass-membership" lang="ta">உறுப்பினர் அடையாள அட்டை</div>
        <div className="pass-portrait"><ProtectedImage path={card.profilePhotoPath} ownerId={card.id} alt="Member passport size photo" /></div>
        <dl className="pass-front-fields">
          <Field label="பெயர் / Name" value={card.name} className="pass-name" />
          <Field label="வகை / Membership" value="Member / உறுப்பினர்" />
          <Field label="மாவட்டம் / District" value={card.district} />
          <Field label="உறுப்பினர் எண் / Member ID" value={text(displayId, 'Awaiting assignment')} className="pass-id" />
        </dl>
        <p className="pass-member-status">{text(card.status, 'Member')}{card.verificationStatus && <> · {card.verificationStatus}</>}</p>
        <div className="pass-signatures" aria-label="Reserved officer signature areas">
          {[
            ['president', 'முரளி', 'மாநில தலைவர்', 'State President'],
            ['secretary', 'கே. சங்கர்', 'மாநில செயலாளர்', 'State Secretary'],
            ['treasurer', 'எஸ். விக்னேஷ்', 'மாநில பொருளாளர்', 'State Treasurer']
          ].map(([officer, name, roleTamil, roleEnglish]) =>
            <div className={`pass-signatures__${officer}`} key={officer}>
              <ProtectedOfficerSignature officer={officer} ownerId={card.id} />
              <i /><strong lang="ta">{name}</strong><small lang="ta">{roleTamil}</small><small>{roleEnglish}</small>
            </div>
          )}
        </div>
        <footer className="pass-footer">PRIVATE UNION MEMBERSHIP CARD</footer>
      </article>
      <article className="member-pass member-pass--back" data-card-side="back" aria-label="Back of membership card">
        <header className="pass-back-heading"><strong>{branding.nameEnglish}</strong><span>MEMBER PARTICULARS · BACK</span></header>
        <dl className="pass-back-fields">
          <Field label="உறுப்பினர் எண் / Member ID" value={text(displayId, 'Awaiting assignment')} className="pass-display-id" />
          <Field label="பிறந்த தேதி / DOB" value={date(card.dateOfBirth)} />
          <Field label="அலைபேசி / Mobile" value={card.phoneNumber} />
          <Field label="இரத்த வகை / Blood group" value={card.bloodGroup} />
          <Field label="தந்தை பெயர் / Father" value={card.fatherName} />
          <Field label="சேர்ந்த நாள் / Joined" value={date(card.issueDate)} />
          <Field label="செல்லுபடிக் காலம் / Validity" value={card.validityYear} />
          <Field label="வீட்டு முகவரி / House address" value={card.permanentAddress} className="pass-address" />
        </dl>
        {displayId !== card.memberId && card.memberId && <p className="pass-stored-id">Original printed ID: <strong>{card.memberId}</strong></p>}
        {card.memberIdDisplayWarning && <p className="pass-id-warning" role="status">{card.memberIdDisplayWarning}</p>}
        <section className="pass-organization-contact" aria-label="Organization social and website details">
          <dl>{organizationContactRows().map(({ label, value }) => <div key={label}><dt><OrganizationContactIcon label={label} /></dt><dd>{value}</dd></div>)}</dl>
        </section>
        <footer className="pass-footer pass-return-note"><p lang="ta">இந்த அட்டை சங்க உறுப்பினர் அடையாளத்திற்கானது. கண்டெடுக்கப்பட்டால் சங்க அலுவலகத்தைத் தொடர்பு கொள்ளவும்.</p><span>Private union membership · Not a government ID</span></footer>
      </article>
    </div>
  </div>;
});

async function ready(root) {
  const deadline = Date.now() + 12000;
  while (root.querySelector('[data-image-pending="true"]')) {
    if (Date.now() > deadline) throw new Error('Passport photo is still loading. Please retry.');
    await new Promise((resolve) => setTimeout(resolve, 100));
  }
  if (root.querySelector('[data-signature-unavailable="true"]')) throw new Error('Officer signature is unavailable. Please retry.');
  if (document.fonts) await Promise.race([document.fonts.ready, new Promise((resolve) => setTimeout(resolve, 3000))]);
  await Promise.all([...root.querySelectorAll('img')].map((img) => {
    if (img.complete) return Promise.resolve();
    return new Promise((resolve) => {
      const finish = () => { clearTimeout(timer); img.removeEventListener('load', finish); img.removeEventListener('error', finish); resolve(); };
      const timer = setTimeout(finish, 10000);
      img.addEventListener('load', finish); img.addEventListener('error', finish);
    });
  }));
}

export async function downloadIdCard(root, memberId) {
  if (!root) throw new Error('Card preview is not ready.');
  await ready(root);
  const { default: html2canvas } = await import('html2canvas');
  // A fixed export size keeps the downloaded card identical on mobile and desktop.
  const clone = root.cloneNode(true);
  clone.classList.add('card-pair--export');
  Object.assign(clone.style, { position: 'fixed', left: '-10000px', top: '0', width: '760px', margin: '0' });
  clone.setAttribute('aria-hidden', 'true');
  document.body.appendChild(clone);
  try {
    await ready(clone);
    const sides = [...clone.querySelectorAll('[data-card-side]')];
    if (sides.length !== 2) throw new Error('Both card sides are required.');
    const height = Math.ceil(Math.max(...sides.map((side) => Math.max(side.scrollHeight, side.getBoundingClientRect().height))));
    sides.forEach((side) => { side.style.height = `${height}px`; });
    const canvases = [];
    for (const side of sides) canvases.push(await html2canvas(side, { backgroundColor: '#FFF8E5', scale: 2, useCORS: true, imageTimeout: 10000, logging: false }));
    const safeId = memberIdFilePart(memberId);
    const widthMm = 54;
    const heightMm = widthMm * canvases[0].height / canvases[0].width;
    try {
      const { jsPDF } = await import('jspdf');
      const pdf = new jsPDF({ orientation: 'portrait', unit: 'mm', format: [widthMm, heightMm], compress: true });
      canvases.forEach((canvas, index) => {
        if (index) pdf.addPage([widthMm, heightMm], 'portrait');
        pdf.addImage(canvas.toDataURL('image/png'), 'PNG', 0, 0, widthMm, heightMm, undefined, 'FAST');
      });
      pdf.save(`WTS-IDCARD-${safeId}.pdf`);
      return { type: 'pdf' };
    } catch {
      const combined = document.createElement('canvas');
      combined.width = canvases[0].width + canvases[1].width + 32;
      combined.height = Math.max(...canvases.map((canvas) => canvas.height));
      const context = combined.getContext('2d');
      context.fillStyle = '#FFF8E5'; context.fillRect(0, 0, combined.width, combined.height);
      context.drawImage(canvases[0], 0, 0); context.drawImage(canvases[1], canvases[0].width + 32, 0);
      const link = document.createElement('a');
      link.href = combined.toDataURL('image/png'); link.download = `WTS-IDCARD-${safeId}.png`;
      document.body.appendChild(link); link.click(); link.remove();
      return { type: 'png' };
    }
  } finally { clone.remove(); }
}

export default MembershipIdCard;
