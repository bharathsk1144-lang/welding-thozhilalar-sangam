import { useEffect, useState } from 'react';
import AppShell from '../components/AppShell.jsx';
import { api } from '../api/client.js';
import ContentImage from '../components/ContentImage.jsx';

export default function ContentListPage({ section }) {
  const [items, setItems] = useState([]);
  useEffect(() => { api.websiteContent().then((content) => setItems(content[section] || [])).catch(() => {}); }, [section]);
  return <AppShell><section className="home-section"><p className="eyebrow">Welding Thozhilalar Sangam</p><h1>{section === 'gallery' ? 'Gallery' : 'Products'}</h1>
    <div className="website-items">{[...items].sort((a, b) => a.order - b.order).map((item) =>
      <article className="panel" key={item.id}>{item.imagePath && <ContentImage path={item.imagePath} alt={item.title} />}<h2>{item.title}</h2><p>{item.description}</p></article>)}
    </div>{items.length === 0 && <p>Information will be added here by the association.</p>}</section></AppShell>;
}
