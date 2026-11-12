import { useEffect, useRef, useState } from 'react';
import { Link, NavLink, useLocation } from 'react-router-dom';
import { branding } from '../config/branding.js';

export default function AppShell({ children, admin = false }) {
  const [menuOpen, setMenuOpen] = useState(false);
  const menuButton = useRef(null);
  const location = useLocation();
  useEffect(() => { setMenuOpen(false); }, [location.pathname]);
  useEffect(() => {
    function onKey(event) {
      if (event.key === 'Escape' && menuOpen) { setMenuOpen(false); menuButton.current?.focus(); }
    }
    document.addEventListener('keydown', onKey);
    return () => document.removeEventListener('keydown', onKey);
  }, [menuOpen]);
  const navClass = ({ isActive }) => isActive ? 'is-active' : undefined;
  return (
    <div className="app-shell sangam-theme">
      <a className="skip-link" href="#main-content">Skip to content / உள்ளடக்கத்திற்குச் செல்ல</a>
      <div className="association-strip">
        <span lang="ta">உழைப்பால் உயர்வோம் · ஒன்றிணைந்து முன்னேறுவோம்</span>
        <Link to="/user-login">உறுப்பினர் சேவைகள் <span aria-hidden="true">↗</span></Link>
      </div>
      <header className="topbar">
        <Link to="/" className="brand" aria-label={`${branding.nameEnglish} home`}>
          <img src={branding.logo} alt="" className="brand-logo" width="62" height="62" />
          <span><strong lang="ta">{branding.nameTamil}</strong><small>{branding.nameEnglish}</small></span>
        </Link>
        <button ref={menuButton} className="menu-toggle" type="button" aria-expanded={menuOpen} aria-controls="main-navigation" onClick={() => setMenuOpen(!menuOpen)}>
          <span aria-hidden="true">{menuOpen ? '×' : '☰'}</span><span>{menuOpen ? 'Close' : 'Menu'}</span>
        </button>
        <nav id="main-navigation" className={`topnav${menuOpen ? ' topnav--open' : ''}`} aria-label="Primary navigation">
          <Link to="/about"><span lang="ta">சங்கம் பற்றி</span><small>About us</small></Link>
          <NavLink className={navClass} to="/verify-member"><span lang="ta">உறுப்பினர் சரிபார்ப்பு</span><small>Verify member</small></NavLink>
          <NavLink className={navClass} to="/user-login"><span lang="ta">உறுப்பினர் உள்நுழைவு</span><small>User login</small></NavLink>
          <NavLink className={({ isActive }) => `nav-enrol${isActive ? ' is-active' : ''}`} to="/register"><span lang="ta">உறுப்பினராக இணைக</span><small>New registration <span aria-hidden="true">↗</span></small></NavLink>
          {admin ? <NavLink to="/admin/dashboard" className={navClass}>Dashboard</NavLink> : <NavLink to="/admin/login" className="nav-admin">Admin</NavLink>}
        </nav>
      </header>
      <main id="main-content" tabIndex="-1">{children}</main>
      <footer className="site-footer">
        <div className="footer-identity"><img src={branding.logo} alt="" width="70" height="70" /><div><strong lang="ta">{branding.nameTamil}</strong><span>{branding.nameEnglish}</span><p lang="ta">வெல்டிங் தொழிலாளர்களின் ஒற்றுமைக்காக.</p></div></div>
        <div><h2 lang="ta">உறுப்பினர் சேவைகள்</h2><Link to="/register">புதிய உறுப்பினர் பதிவு</Link><Link to="/user-login">Login & ID card</Link><Link to="/forgot-password">Forgot password</Link></div>
        <div><h2 lang="ta">சங்கத் தகவல்கள்</h2><Link to="/about">About the Sangam</Link><a href="/#contact">Office & contact</a>{branding.email && <a href={`mailto:${branding.email}`}>{branding.email}</a>}<Link to="/verify-member">Verify a member</Link><Link to="/admin/login">Administration</Link></div>
        <div className="footer-bottom"><span>Welding Thozhilalar Sangam · Private union membership portal</span><span lang="ta">ஒற்றுமையே நம் வலிமை.</span></div>
      </footer>
    </div>
  );
}
