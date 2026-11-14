import { useEffect, useState } from 'react';
import { fileUrl } from '../api/client.js';

export default function ContentImage({ path, alt }) {
  const [src, setSrc] = useState('');
  useEffect(() => {
    if (!/^\/uploads\/content-[^/\\?#:]+$/.test(path || '')) return;
    const controller = new AbortController();
    let url = '';
    fetch(fileUrl(`/api/website-content/images/${path.split('/').pop()}`), { signal: controller.signal })
      .then((response) => { if (!response.ok || !response.headers.get('content-type')?.startsWith('image/')) throw new Error('Image unavailable'); return response.blob(); })
      .then((blob) => { if (!controller.signal.aborted) { url = URL.createObjectURL(blob); setSrc(url); } })
      .catch(() => {});
    return () => { controller.abort(); if (url) URL.revokeObjectURL(url); };
  }, [path]);
  return src ? <img src={src} alt={alt} loading="lazy" /> : null;
}
