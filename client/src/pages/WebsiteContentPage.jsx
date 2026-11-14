import { useEffect, useState } from 'react';
import { Link } from 'react-router-dom';
import AppShell from '../components/AppShell.jsx';
import TamilDocument from '../components/TamilDocument.jsx';
import { api } from '../api/client.js';
import ContentImage from '../components/ContentImage.jsx';

const blank = () => ({ id: crypto.randomUUID(), title: '', description: '', imagePath: '', order: 0 });

export default function WebsiteContentPage() {
  const [content, setContent] = useState(null);
  const [draft, setDraft] = useState(null);
  const [section, setSection] = useState('about');
  const [editing, setEditing] = useState(false);
  const [preview, setPreview] = useState(false);
  const [history, setHistory] = useState([]);
  const [error, setError] = useState('');
  const [message, setMessage] = useState('');
  const [saving, setSaving] = useState(false);
  async function load() {
    const [data, changes] = await Promise.all([api.websiteContent(), api.contentHistory()]);
    setContent(data); setDraft(structuredClone(data)); setHistory(changes);
  }
  useEffect(() => { load().catch((err) => setError(err.message)); }, []);
  function select(next) { setSection(next); setDraft(structuredClone(content)); setEditing(false); setPreview(false); setError(''); }
  function updateItem(id, field, value) { setDraft((current) => ({ ...current, [section]: current[section].map((item) => item.id === id ? { ...item, [field]: value } : item) })); }
  async function upload(id, file) {
    if (!file) return;
    setError('');
    try { const result = await api.uploadContentImage(file); updateItem(id, 'imagePath', result.imagePath); }
    catch (err) { setError(err.message); }
  }
  async function save() {
    setSaving(true); setError(''); setMessage('');
    try {
      await api.saveWebsiteContent({ section, revision: content.revision, aboutMarkdown: draft.aboutMarkdown, aboutContact: draft.aboutContact, items: section === 'about' ? undefined : draft[section] });
      await load(); setEditing(false); setPreview(false); setMessage('Website content saved and available publicly.');
    } catch (err) { setError(err.message); }
    finally { setSaving(false); }
  }
  return <AppShell admin><section className="admin-layout website-content-admin">
    <Link to="/admin/dashboard">Back to Dashboard</Link><h1>Website Content</h1>
    <p>Edit association information, gallery and products. Public pages are read only.</p>
    {error && <div className="notice error" role="alert">{error}</div>}{message && <div className="notice success" role="status">{message}</div>}
    <nav className="content-tabs" aria-label="Website content sections">{['about', 'gallery', 'products'].map((name) => <button key={name} type="button" aria-current={section === name ? 'page' : undefined} onClick={() => select(name)}>{name[0].toUpperCase() + name.slice(1)}</button>)}</nav>
    {draft && <section className="panel data-panel">
      <div className="detail-actions">{!editing ? <button type="button" className="primary" onClick={() => setEditing(true)}>Edit</button> : <>
        <button type="button" onClick={() => setPreview(!preview)}>{preview ? 'Edit' : 'Preview'}</button>
        <button type="button" className="primary" disabled={saving} onClick={save}>{saving ? 'Saving...' : 'Save'}</button>
        <button type="button" onClick={() => { setDraft(structuredClone(content)); setEditing(false); setPreview(false); }}>Cancel</button></>}</div>
      {section === 'about' && (editing && !preview ? <><label>About page and full constitution<textarea className="content-editor" value={draft.aboutMarkdown} onChange={(event) => setDraft({ ...draft, aboutMarkdown: event.target.value })} /></label><label>Contact details<textarea value={draft.aboutContact || ''} onChange={(event) => setDraft({ ...draft, aboutContact: event.target.value })} /></label></> : <><TamilDocument source={draft.aboutMarkdown} tableOfContents />{draft.aboutContact && <section><h2>Contact</h2><p className="content-contact">{draft.aboutContact}</p></section>}</>)}
      {section !== 'about' && <div className="website-items">{[...draft[section]].sort((a, b) => a.order - b.order).map((item) => <article className="panel" key={item.id}>
        {item.imagePath && <ContentImage path={item.imagePath} alt={item.title} />}
        {editing && !preview ? <>
          <label>Title<input maxLength={150} value={item.title} onChange={(event) => updateItem(item.id, 'title', event.target.value)} /></label>
          <label>Description<textarea maxLength={2000} value={item.description} onChange={(event) => updateItem(item.id, 'description', event.target.value)} /></label>
          <label>Display order<input type="number" min="0" max="10000" value={item.order} onChange={(event) => updateItem(item.id, 'order', Number(event.target.value))} /></label>
          <label>{item.imagePath ? 'Replace image' : 'Add image'}<input type="file" accept="image/jpeg,image/png,image/webp" onChange={(event) => upload(item.id, event.target.files?.[0])} /></label>
          <button type="button" onClick={() => { if (window.confirm(`Remove ${item.title || 'this item'}?`)) setDraft((current) => ({ ...current, [section]: current[section].filter((entry) => entry.id !== item.id) })); }}>Remove</button>
        </> : <><h2>{item.title}</h2><p>{item.description}</p></>}
      </article>)}
      {editing && !preview && <button type="button" onClick={() => setDraft((current) => ({ ...current, [section]: [...current[section], { ...blank(), order: current[section].length }] }))}>Add {section === 'gallery' ? 'image' : 'product'}</button>}</div>}
    </section>}
    <section className="panel data-panel"><h2>Change history</h2><ul>{history.slice().reverse().slice(0, 30).map((entry) => <li key={entry.revision}>{entry.section} · {entry.editor} · {new Date(entry.timestamp).toLocaleString()}</li>)}</ul></section>
  </section></AppShell>;
}
