import { useEffect, useState } from 'react';
import { privateOfficerSignature } from '../api/client.js';

export default function ProtectedOfficerSignature({ officer, ownerId }) {
  const [state, setState] = useState({ key: '', url: '', loading: false });
  const key = `${officer}:${ownerId || ''}`;
  useEffect(() => {
    const controller = new AbortController();
    let url;
    setState({ key, url: '', loading: !!ownerId });
    if (ownerId) privateOfficerSignature(officer, ownerId, controller.signal).then((blob) => {
      if (controller.signal.aborted) return;
      url = URL.createObjectURL(blob);
      setState({ key, url, loading: false });
    }).catch(() => {
      if (!controller.signal.aborted) setState({ key, url: '', loading: false });
    });
    return () => { controller.abort(); if (url) URL.revokeObjectURL(url); };
  }, [key, officer, ownerId]);
  const current = state.key === key ? state : { url: '', loading: !!ownerId };
  return current.url
    ? <img src={current.url} alt={`${officer} signature`} />
    : <span className="pass-signature-blank" data-image-pending={current.loading ? 'true' : undefined}
        data-signature-unavailable={ownerId && !current.loading ? 'true' : undefined} aria-hidden="true" />;
}
