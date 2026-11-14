import AppShell from '../components/AppShell.jsx';
import TamilDocument from '../components/TamilDocument.jsx';
import content from '../content/about-tamil.md?raw';
import { useEffect, useState } from 'react';
import { api } from '../api/client.js';

export default function AboutPage() {
  const [source, setSource] = useState(content);
  const [contact, setContact] = useState('');
  useEffect(() => { api.websiteContent().then((value) => { if (value.aboutMarkdown) setSource(value.aboutMarkdown); setContact(value.aboutContact || ''); }).catch(() => {}); }, []);
  return <AppShell><section className="about-page"><TamilDocument source={source} tableOfContents />{contact && <section className="panel data-panel"><h2>Contact</h2><p className="content-contact">{contact}</p></section>}</section></AppShell>;
}
