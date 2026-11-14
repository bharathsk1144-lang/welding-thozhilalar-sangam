// Model, runtime, worker, and language files are served from this website.
// Member images are processed in memory in the browser and never sent to a model API.
let detectorPromise;
let ocrPromise;

export async function detectFacesLocally(image) {
  detectorPromise ??= (async () => {
    const { FaceDetector, FilesetResolver } = await import('@mediapipe/tasks-vision');
    const files = await FilesetResolver.forVisionTasks('/vision');
    return FaceDetector.createFromOptions(files, {
      baseOptions: { modelAssetPath: '/vision/blaze_face_short_range.tflite', delegate: 'CPU' },
      runningMode: 'IMAGE', minDetectionConfidence: 0.55
    });
  })().catch((error) => { detectorPromise = undefined; throw error; });
  const detector = await detectorPromise;
  return detector.detect(image).detections.map(({ boundingBox: box }) => ({
    x: box.originX, y: box.originY, width: box.width, height: box.height
  }));
}

export async function readDocumentTextLocally(image) {
  if (typeof process !== 'undefined' && process.versions?.node) throw new Error('Browser OCR is unavailable in Node tests.');
  ocrPromise ??= (async () => {
    const { createWorker } = await import('tesseract.js');
    return createWorker('eng', 1, {
      workerPath: '/ocr/worker.min.js', corePath: '/ocr', langPath: '/ocr',
      workerBlobURL: false
    });
  })().catch((error) => { ocrPromise = undefined; throw error; });
  const worker = await ocrPromise;
  const scale = Math.min(1, 1600 / Math.max(image.width, image.height));
  const canvas = document.createElement('canvas');
  canvas.width = Math.max(1, Math.round(image.width * scale));
  canvas.height = Math.max(1, Math.round(image.height * scale));
  canvas.getContext('2d').drawImage(image, 0, 0, canvas.width, canvas.height);
  const { data } = await worker.recognize(canvas);
  return data.text || '';
}
