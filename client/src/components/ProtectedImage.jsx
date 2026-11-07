import { useEffect, useState } from 'react';
import { privateImage } from '../api/client.js';

export default function ProtectedImage({ path, ownerId, adminOnly = false, alt, className = '' }) {
  const [state, setState] = useState({ key: '', url: '', loading: false });
  const key = `${path}:${ownerId}:${adminOnly}`;
  useEffect(() => {
    const controller = new AbortController();
    let url;
    setState({ key, url: '', loading: !!path });
    if (path) privateImage(path, ownerId, adminOnly, controller.signal).then((blob) => {
      if (controller.signal.aborted) return;
      url = URL.createObjectURL(blob);
      setState({ key, url, loading: false });
    }).catch(() => {
      if (!controller.signal.aborted) setState({ key, url: '', loading: false });
    });
    return () => { controller.abort(); if (url) URL.revokeObjectURL(url); };
  }, [path, ownerId, adminOnly, key]);
  const current = state.key === key ? state : { url: '', loading: !!path };
  return current.url
    ? <img className={className} src={current.url} alt={alt} onError={() => setState({ key, url: '', loading: false })} />
    : <div className={`image-placeholder ${className}`} data-image-pending={current.loading ? 'true' : undefined} role="img" aria-label={alt}>
      {current.loading ? 'Loading image…' : 'Image unavailable'}
    </div>;
}
