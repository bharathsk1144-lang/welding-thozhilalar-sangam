import { Link } from 'react-router-dom';
import AppShell from '../components/AppShell.jsx';
import MembershipStatistics from '../components/MembershipStatistics.jsx';
import { branding } from '../config/branding.js';

function Mark({ type = 'spark' }) {
  const paths = { spark: 'M12 2v5m0 10v5M2 12h5m10 0h5M5 5l3 3m8 8 3 3M5 19l3-3m8-8 3-3', card: 'M3 5h18v14H3zM7 9h3v4H7zm7 0h4m-4 4h4M7 16h11', district: 'M12 22s8-8 8-13a8 8 0 1 0-16 0c0 5 8 13 8 13zM9 9a3 3 0 1 0 6 0 3 3 0 0 0-6 0', shield: 'm12 2 9 4v6c0 5-9 10-9 10S3 17 3 12V6zM8 12l3 3 5-6' };
  return <svg aria-hidden="true" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.4" strokeLinecap="round" strokeLinejoin="round"><path d={paths[type]} /></svg>;
}

export default function HomePage() {
  return (
    <AppShell>
      <section className="sangam-hero" aria-labelledby="home-title">
        <div className="sangam-hero__copy">
          <p className="eyebrow"><span className="gold-rule" /> WELDING THOZHILALAR SANGAM</p>
          <h1 id="home-title" lang="ta">உழைக்கும் கரங்கள்.<br /><em>ஒன்றிணையும் வலிமை.</em></h1>
          <p className="hero-tamil" lang="ta">வெல்டிங் தொழிலாளர்களின் அடையாளம், ஒற்றுமை, முன்னேற்றம் — நம் சங்கத்தின் வழியே.</p>
          <p className="hero-english">A shared identity for the hands that build. Join the Sangam, connect with your district, and keep your membership close.</p>
          <div className="hero-actions"><Link to="/register" className="button-link primary"><span lang="ta">உறுப்பினராக இணைக</span><span aria-hidden="true">↗</span></Link><Link to="/user-login" className="button-link hero-login">Member login <span aria-hidden="true">→</span></Link></div>
          <div className="hero-assurance"><Mark type="shield" /><span>Mobile verified registration <i /> Your own membership ID card</span></div>
        </div>
        <div className="sangam-hero__emblem"><div className="emblem-orbit" aria-hidden="true" /><img src={branding.logo} alt="Welding Thozhilalar Sangam welding-worker crest" width="420" height="420" fetchPriority="high" /><p lang="ta">உழைப்பு · ஒற்றுமை · முன்னேற்றம்</p><span>OUR CRAFT. OUR IDENTITY. OUR SANGAM.</span></div>
        <div className="hero-bottom-line"><span>THE STRENGTH BEHIND EVERY JOINT</span><a href="#about">Explore the Sangam <span aria-hidden="true">↓</span></a></div>
      </section>

      <MembershipStatistics />

      <section className="home-about home-section" id="about">
        <div className="section-number" aria-hidden="true">01 / OUR SANGAM</div>
        <div><p className="eyebrow">ஒற்றுமையால் முன்னேற்றம்</p><h2 lang="ta">நம் தொழில்.<br />நம் அடையாளம்.</h2></div>
        <div className="about-copy"><p lang="ta">வெல்டிங் தொழிலாளர்களை ஒருங்கிணைக்கும் உறுப்பினர் தளம். உங்கள் விவரங்களைப் பதிவு செய்து, மாவட்டக் குழுவுடன் இணைந்து, சங்க உறுப்பினர் அட்டையை எளிதாக அணுகுங்கள்.</p><p>Welding Thozhilalar Sangam brings membership registration, district connections and a secure digital member card into one place. An identity built around the work we share.</p><a href="#member-services" className="text-link">Discover member services <span aria-hidden="true">↗</span></a></div>
      </section>

      <section id="member-services" className="home-services home-section">
        <div className="section-heading"><div><p className="eyebrow">FOR EVERY MEMBER</p><h2 lang="ta">உறுப்பினருக்கான சேவைகள்</h2></div><p>Simple access. A clear identity.<br />Connected to your district.</p></div>
        <div className="service-grid">
          <article><Mark type="shield" /><span className="service-index">01</span><h3 lang="ta">உறுப்பினர் பதிவு</h3><p>Verify your mobile once and complete your membership details securely.</p><Link to="/register" className="text-link">New registration <span aria-hidden="true">↗</span></Link></article>
          <article><Mark type="card" /><span className="service-index">02</span><h3 lang="ta">உறுப்பினர் அடையாள அட்டை</h3><p>View both sides of your membership card and download a copy when you need it.</p><Link to="/user-login" className="text-link">Access your ID card <span aria-hidden="true">↗</span></Link></article>
          <article><Mark type="district" /><span className="service-index">03</span><h3 lang="ta">மாவட்ட இணைப்பு</h3><p>Select your district during registration and join its listed WhatsApp group.</p><a href="#districts" className="text-link">District information <span aria-hidden="true">↗</span></a></article>
        </div>
      </section>

      <section className="membership-banner home-section"><div><p className="eyebrow">YOUR NEXT STEP</p><h2 lang="ta">சங்கத்தில் இணைவோம்.<br />ஒன்றாக முன்னேறுவோம்.</h2><p>Start with your name and mobile. Verify your OTP, then complete your details.</p></div><div className="membership-banner__action"><Link to="/register" className="button-link primary">உறுப்பினர் பதிவு <span aria-hidden="true">↗</span></Link><Link to="/user-login">Already a member? Login →</Link></div></section>

      <section id="districts" className="home-organization home-section">
        <div><p className="eyebrow">CONNECTED THROUGH OUR DISTRICTS</p><h2 lang="ta">உங்கள் மாவட்டம்.<br />உங்கள் சங்கத் தொடர்பு.</h2><p>Choose the appropriate district in the registration form. District group links are maintained by the administration.</p><Link to="/register" className="text-link">Find your district during registration ↗</Link></div>
        <div className="organization-placeholder"><Mark type="district" /><span className="content-pending">CLIENT CONTENT PENDING</span><h3 lang="ta">மாவட்ட அமைப்புத் தகவல்கள்</h3><p lang="ta">மாவட்ட நிர்வாகிகள் மற்றும் விரிவாக்கத் தகவல்கள் சங்க நிர்வாகத்தால் சேர்க்கப்பட உள்ளன.</p><small>District coordinators and organization details will be published after confirmation.</small></div>
      </section>

      <section className="home-history home-section"><div className="section-number">OUR STORY</div><h2 lang="ta">சங்கத்தின் பயணம்</h2>{branding.history ? <p>{branding.history}</p> : <p>Our founding story, start date, founder details and achievements will be added here after the Sangam supplies and confirms them.</p>}<span className="content-pending">{branding.history ? 'ORGANIZATION HISTORY' : 'EDITABLE CONTENT · AWAITING SANGAM DETAILS'}</span>{branding.foundedOn && <p>Established: {branding.foundedOn}</p>}</section>

      <section id="contact" className="home-contact home-section"><div><p className="eyebrow">GET IN TOUCH</p><h2 lang="ta">சங்க அலுவலகம்<br />மற்றும் தொடர்பு</h2></div><div><h3>Office address</h3><p>{branding.officeAddress || 'Office address will be published after confirmation by the Sangam.'}</p>{branding.phone && <a href={`tel:${branding.phone}`}>{branding.phone}</a>}{branding.email && <a href={`mailto:${branding.email}`}>{branding.email}</a>}{branding.officeHours && <p>{branding.officeHours}</p>}{!branding.officeAddress && <span className="content-pending">CONTACT DETAILS AWAITING CONFIRMATION</span>}<Link to="/verify-member" className="text-link">Need to verify a membership? ↗</Link></div></section>
    </AppShell>
  );
}
