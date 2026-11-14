const TYPES = { 'image/jpeg': [0xff, 0xd8, 0xff], 'image/png': [0x89, 0x50, 0x4e, 0x47], 'image/webp': [0x52, 0x49, 0x46, 0x46] };

async function facesIn(bitmap) {
  if (typeof window.FaceDetector === 'function') {
    const found = await new window.FaceDetector({ fastMode: false, maxDetectedFaces: 5 }).detect(bitmap);
    return found.map((face) => face.boundingBox);
  }
  const { detectFacesLocally } = await import('./localVision.js');
  return detectFacesLocally(bitmap);
}

async function textIn(bitmap) {
  if (typeof window.TextDetector === 'function') {
    const blocks = await new window.TextDetector().detect(bitmap);
    return blocks.map((block) => block.rawValue || '').join(' ');
  }
  const { readDocumentTextLocally } = await import('./localVision.js');
  return readDocumentTextLocally(bitmap);
}

function sampleQuality(bitmap) {
  if (typeof document === 'undefined') return null;
  try {
    const canvas = document.createElement('canvas'); canvas.width = 64; canvas.height = 64;
    const context = canvas.getContext('2d', { willReadFrequently: true });
    context.drawImage(bitmap, 0, 0, 64, 64);
    const pixels = context.getImageData(0, 0, 64, 64).data;
    let detail = 0, saturated = 0, borderDetail = 0;
    for (let y = 1; y < 63; y++) for (let x = 1; x < 63; x++) {
      const i = (y * 64 + x) * 4;
      const light = (pixels[i] + pixels[i + 1] + pixels[i + 2]) / 3;
      const right = (pixels[i + 4] + pixels[i + 5] + pixels[i + 6]) / 3;
      const difference = Math.abs(light - right);
      detail += difference;
      if (x < 12 || x > 51) borderDetail += difference;
      if (Math.max(pixels[i], pixels[i + 1], pixels[i + 2]) - Math.min(pixels[i], pixels[i + 1], pixels[i + 2]) > 150) saturated++;
    }
    return { detail: detail / (62 * 62), saturated: saturated / (62 * 62), borderDetail: borderDetail / (62 * 22) };
  } catch { return null; }
}

export async function inspectImage(file, kind) {
  const label = kind === 'passport' ? 'Passport photo' : 'Aadhaar image';
  if (!file || !TYPES[file.type]) return { error: `${label} must be a JPG, PNG, or WEBP image.` };
  if (!file.size || file.size > (kind === 'passport' ? 3 : 5) * 1024 * 1024) return { error: `${label} exceeds the size limit or is empty.` };
  const bytes = new Uint8Array(await file.slice(0, 12).arrayBuffer());
  if (!TYPES[file.type].every((byte, index) => bytes[index] === byte) || (file.type === 'image/webp' && String.fromCharCode(...bytes.slice(8, 12)) !== 'WEBP')) return { error: `${label} contents do not match its file type.` };
  let bitmap;
  try { bitmap = await createImageBitmap(file); } catch { return { error: `${label} is damaged or unreadable.` }; }
  try {
    const { width, height } = bitmap;
    if (width < 320 || height < 240 || width > 10000 || height > 10000 || width * height > 40_000_000) return { error: `${label} dimensions are unsuitable.` };
    const ratio = width / height;
    const quality = sampleQuality(bitmap);
    if (quality && quality.detail < 0.8) return { error: `${label} appears blank or severely blurred. Choose a clearer image.` };
    if (kind === 'passport') {
      if (ratio < 0.45 || ratio > 1.15) return { error: 'Choose a portrait photo showing only your head and shoulders, or crop this photo.' };
      let faceWarning = '';
      {
        try {
          const faces = await facesIn(bitmap);
          if (faces.length > 1) return { error: 'More than one face was found. Choose a photo of yourself only.' };
          if (faces.length === 0) faceWarning = 'A face was not detected reliably. ';
          else {
            const box = faces[0];
            const center = (box.x + box.width / 2) / width;
            const faceSize = box.height / height;
            if (faceSize < 0.17 || center < 0.25 || center > 0.75) return { error: 'Face is too small or off centre. Choose a head-and-shoulders photo or crop it.' };
          }
        } catch { /* Browser face detection may be unavailable; admin reviews uncertain photos. */ }
      }
      const qualityWarning = quality?.saturated > 0.12 ? 'Heavy color effects may make the photo unsuitable. ' :
        quality?.borderDetail > 22 ? 'Background may be too busy. ' :
        quality?.detail < 4 ? 'Photo may be blurred. ' : '';
      return { warning: `${faceWarning}${qualityWarning}Check that one clear face, shoulders, and a plain background are visible. An admin will review the photo.` };
    }
    if (ratio < 0.4 || ratio > 3) return { error: 'Document looks severely cropped. Choose a clear Aadhaar image.' };
    let largestFace = 0;
    try {
        const faces = await facesIn(bitmap);
        largestFace = Math.max(0, ...faces.map((face) => face.height / height));
        if (largestFace > 0.45) return { error: 'This appears to be a portrait rather than an Aadhaar card. Choose the document image.' };
    } catch { /* Review when local detector cannot run. */ }
    try {
        // Check category words only. Never retain or log recognized personal data.
        const text = (await textIn(bitmap)).toLowerCase();
        if (/\b(pan card|driving licence|driving license|passport)\b/.test(text) && !/aadhaar|aadhar|uidai|unique identification|government of india/.test(text))
          return { error: 'This appears to be a different document. Choose your Aadhaar image.' };
        if (/aadhaar|aadhar|uidai|unique identification/.test(text))
          return { warning: 'Aadhaar-related text was detected locally. Check that all details and card edges are readable; an admin will review the document.' };
    } catch { /* Uncertain documents require admin review. */ }
    if (largestFace > 0.28) return { error: 'This appears to be a portrait rather than an Aadhaar card. Choose a clear document image.' };
    return { warning: 'Check that the Aadhaar text and all card edges are readable. An admin will review the document.' };
  } finally { bitmap.close(); }
}
