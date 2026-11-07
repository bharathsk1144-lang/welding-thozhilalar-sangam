import React from 'react';
import ReactDOM from 'react-dom/client';
import { BrowserRouter, Link, Route, Routes, useLocation } from 'react-router-dom';
import HomePage from '../src/pages/HomePage.jsx';
import AppShell from '../src/components/AppShell.jsx';
import '../src/styles.css';
import '../src/styles/redesign.css';
import './preview.css';
import DemoRegistration from './DemoRegistration.jsx';

function Unavailable() {
  const { pathname } = useLocation();
  const message = pathname === '/register'
    ? 'Preview Mode — Registration will be available after launch'
    : pathname.startsWith('/admin')
      ? 'Preview Mode — Administration is unavailable in this design preview'
      : 'Preview Mode — Member services will be available after launch';
  return <AppShell><section className="preview-unavailable"><div className="form-panel">
    <p className="eyebrow">WTS CLIENT DESIGN PREVIEW</p><h1>{message}</h1>
    <p>You can explore the Sangam website design. Account actions and private member information are unavailable in this preview.</p>
    <Link to="/" className="button-link primary">Explore the homepage</Link>
    <a href="/#contact" className="text-link">Organization information</a>
  </div></section></AppShell>;
}

ReactDOM.createRoot(document.getElementById('root')).render(<React.StrictMode><BrowserRouter>
  <div className="client-preview-banner" role="status">Client Preview · {__WTS_PREVIEW_DEMO__ ? 'Sample registration only · No accounts are saved' : 'Design review only · Member services are disabled'}</div>
  <Routes>
    <Route path="/" element={<HomePage />} />
    <Route path="/about" element={<HomePage />} />
    <Route path="/contact" element={<HomePage />} />
    <Route path="/districts" element={<HomePage />} />
    {__WTS_PREVIEW_DEMO__ && <Route path="/register" element={<DemoRegistration />} />}
    <Route path="*" element={<Unavailable />} />
  </Routes>
</BrowserRouter></React.StrictMode>);
