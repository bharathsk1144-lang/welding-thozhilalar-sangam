import { useEffect, useRef, useState } from 'react';

export default function PhotoCropper({ file, onSave, onCancel }) {
  const canvas = useRef(null);
  const image = useRef(null);
  const [zoom, setZoom] = useState(1);
  const [x, setX] = useState(0);
  const [y, setY] = useState(0);
  const [ready, setReady] = useState(false);
  const [saving, setSaving] = useState(false);
  const [error, setError] = useState('');
  useEffect(() => {
    setReady(false);
    setError('');
    let disposed = false;
    const url = URL.createObjectURL(file);
    const loaded = new Image();
    loaded.onload = () => { if (!disposed) { image.current = loaded; setError(''); setReady(true); } };
    loaded.onerror = () => { if (!disposed) setError('This photo could not be opened. Choose another photo.'); };
    loaded.src = url;
    return () => { disposed = true; loaded.onload = null; loaded.onerror = null; URL.revokeObjectURL(url); image.current = null; };
  }, [file]);
  useEffect(() => {
    if (!ready || !canvas.current || !image.current) return;
    const source = image.current, target = canvas.current;
    const context = target.getContext('2d');
    const scale = Math.max(target.width / source.width, target.height / source.height) * zoom;
    const width = source.width * scale, height = source.height * scale;
    context.fillStyle = 'white'; context.fillRect(0, 0, target.width, target.height);
    context.drawImage(source, (target.width - width) / 2 + x * (width - target.width) / 200,
      (target.height - height) / 2 + y * (height - target.height) / 200, width, height);
  }, [ready, zoom, x, y]);
  async function save() {
    setSaving(true);
    try {
      const blob = await new Promise((resolve) => canvas.current.toBlob(resolve, 'image/jpeg', 0.9));
      if (!blob) throw new Error('Could not crop the image.');
      await onSave(new File([blob], 'passport-cropped.jpg', { type: 'image/jpeg' }));
    } finally { setSaving(false); }
  }
  return <div className="photo-cropper" role="dialog" aria-label="Crop passport photo">
    {error && <p className="notice error" role="alert">{error}</p>}
    <canvas ref={canvas} width="600" height="750" aria-label="Passport crop preview" />
    <label>Zoom <input type="range" min="1" max="3" step="0.05" value={zoom} onChange={(event) => setZoom(Number(event.target.value))} /></label>
    <label>Horizontal position <input type="range" min="-100" max="100" value={x} onChange={(event) => setX(Number(event.target.value))} /></label>
    <label>Vertical position <input type="range" min="-100" max="100" value={y} onChange={(event) => setY(Number(event.target.value))} /></label>
    <div className="detail-actions"><button type="button" onClick={() => { setZoom(1); setX(0); setY(0); }}>Reset</button><button type="button" className="primary" disabled={!ready || saving} onClick={save}>Save photo</button><button type="button" onClick={onCancel}>Cancel</button></div>
  </div>;
}
