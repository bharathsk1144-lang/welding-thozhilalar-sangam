import assert from 'node:assert/strict';
import { spawn } from 'node:child_process';
import { createServer } from 'node:http';
import { existsSync, readFileSync } from 'node:fs';
import { mkdir, readFile, readdir } from 'node:fs/promises';
import path from 'node:path';

// Builds the real production frontend against an isolated, in-memory synthetic API.
// No production account or database is accessed.
const root = process.cwd();
const output = path.join(root, 'artifacts', `csp-card-${Date.now()}`);
await mkdir(output, { recursive: true });
const id = '000000000000000000000001';
const card = { id, name: 'Synthetic CSP Member', memberId: 'WTS-2026-000001', displayMemberId: 'WTS-ALR-0001',
  district: 'Ariyalur', phoneNumber: '9876543200', dateOfBirth: '1990-01-01', fatherName: 'Synthetic Father',
  bloodGroup: 'O+', issueDate: '2026-11-12', expiresAt: '2029-11-12', isApproved: true,
  profilePhotoPath: '/uploads/synthetic-profile.png', permanentAddress: 'Synthetic Test Address', status: 'Active Member' };
const mock = createServer((req, res) => {
  res.setHeader('Access-Control-Allow-Origin', '*');
  res.setHeader('Access-Control-Allow-Headers', 'Authorization, Content-Type');
  if (req.method === 'OPTIONS') { res.writeHead(204).end(); return; }
  const signature = req.url?.match(new RegExp(`^/api/users/${id}/officer-signatures/(president|secretary|treasurer)$`));
  if (req.url === `/api/users/${id}/id-card`) {
    res.setHeader('Content-Type', 'application/json'); res.end(JSON.stringify(card)); return;
  }
  if (req.url === '/api/auth/registration-options') { res.setHeader('Content-Type', 'application/json'); res.end('{"otpEnabled":false}'); return; }
  if (req.url === '/api/districts') { res.setHeader('Content-Type', 'application/json'); res.end('[]'); return; }
  if (req.url === '/synthetic-passport.png') {
    res.setHeader('Content-Type', 'image/png');
    res.end(readFileSync(path.join(root, 'tests/fixtures/synthetic-passport.png'))); return;
  }
  if (req.url === '/uploads/synthetic-profile.png') {
    res.setHeader('Content-Type', 'image/png');
    res.end(readFileSync(path.join(root, 'client/public/wts-logo-client-transparent.png'))); return;
  }
  if (signature) {
    res.setHeader('Content-Type', 'image/png');
    res.end(readFileSync(path.join(root, 'server/Assets/OfficerSignatures', `state-${signature[1]}-signature-blue.png`))); return;
  }
  res.writeHead(404).end();
});
await new Promise(resolve => mock.listen(0, '127.0.0.1', resolve));
const apiBase = `http://127.0.0.1:${mock.address().port}/api`;
const built = spawn(process.execPath, [path.join(root, 'client/node_modules/vite/bin/vite.js'), 'build'], {
  cwd: path.join(root, 'client'), windowsHide: true, stdio: 'ignore', env: { ...process.env, VITE_API_BASE_URL: apiBase }
});
const buildCode = await new Promise(resolve => built.once('exit', resolve));
assert.equal(buildCode, 0, 'production Vite build');
const dist = path.join(root, 'client/dist');
const rawPolicy = JSON.parse(readFileSync(path.join(root, 'client/vercel.json'), 'utf8')).headers[0].headers
  .find(header => header.key.startsWith('Content-Security-Policy')).value;
const policy = rawPolicy.replace('connect-src ', `connect-src ${apiBase.replace(/\/api$/, '')} `)
  .replace('; upgrade-insecure-requests', ''); // local HTTP fixture has no TLS
const policyName = process.env.WTS_CSP_ENFORCE === '1' ? 'Content-Security-Policy' : 'Content-Security-Policy-Report-Only';
const frontend = createServer((req, res) => {
  const pathname = decodeURIComponent(new URL(req.url, 'http://localhost').pathname);
  const candidate = path.resolve(dist, `.${pathname}`);
  const file = candidate.startsWith(`${dist}${path.sep}`) && existsSync(candidate) ? candidate : path.join(dist, 'index.html');
  const ext = path.extname(file);
  res.setHeader(policyName, policy);
  res.setHeader('Content-Type', ({ '.html': 'text/html; charset=utf-8', '.js': 'text/javascript',
    '.css': 'text/css', '.png': 'image/png', '.svg': 'image/svg+xml', '.wasm': 'application/wasm', '.gz': 'application/gzip' })[ext] || 'application/octet-stream');
  res.end(readFileSync(file));
});
await new Promise(resolve => frontend.listen(0, '127.0.0.1', resolve));
const origin = `http://127.0.0.1:${frontend.address().port}`;
const binary = process.env.WTS_TEST_BROWSER || [
  'C:/Program Files/Google/Chrome/Application/chrome.exe',
  'C:/Program Files (x86)/Microsoft/Edge/Application/msedge.exe'
].find(existsSync);
assert.ok(binary, 'Chrome or Edge is required');
const profile = path.join(output, 'chrome-profile');
await mkdir(profile);
const browser = spawn(binary, ['--headless=new', '--disable-gpu', '--no-first-run', '--no-default-browser-check',
  '--disable-background-networking', '--remote-debugging-port=0', `--user-data-dir=${profile}`, 'about:blank'],
{ windowsHide: true, stdio: 'ignore' });
const pause = ms => new Promise(resolve => setTimeout(resolve, ms));
async function until(action, label, timeout = 45000) {
  const end = Date.now() + timeout;
  while (Date.now() < end) {
    try { const value = await action(); if (value) return value; } catch { }
    await pause(150);
  }
  throw new Error(`Timed out: ${label}`);
}
let socket;
try {
  const port = await until(async () => (await readFile(path.join(profile, 'DevToolsActivePort'), 'utf8')).split('\n')[0], 'browser startup');
  const target = await (await fetch(`http://127.0.0.1:${port}/json/new?about:blank`, { method: 'PUT' })).json();
  socket = new WebSocket(target.webSocketDebuggerUrl);
  await new Promise((resolve, reject) => { socket.onopen = resolve; socket.onerror = reject; });
  let idCounter = 0;
  const pending = new Map();
  socket.onmessage = ({ data }) => {
    const result = JSON.parse(data);
    if (!result.id || !pending.has(result.id)) return;
    const item = pending.get(result.id); pending.delete(result.id); clearTimeout(item.timer);
    result.error ? item.reject(new Error(result.error.message)) : item.resolve(result.result);
  };
  const command = (method, params = {}) => new Promise((resolve, reject) => {
    const key = ++idCounter;
    const timer = setTimeout(() => { pending.delete(key); reject(new Error(`Timed out: ${method}`)); }, 45000);
    pending.set(key, { resolve, reject, timer }); socket.send(JSON.stringify({ id: key, method, params }));
  });
  const imageRequests = [];
  socket.onmessage = ((original) => ({ data }) => {
    const event = JSON.parse(data);
    if (event.method === 'Network.requestWillBeSent' && /\/vision\/|\/ocr\//.test(event.params.request.url)) imageRequests.push(event.params.request.url);
    original({ data });
  })(socket.onmessage);
  const evaluate = async expression => {
    const result = await command('Runtime.evaluate', { expression, awaitPromise: true, returnByValue: true });
    if (result.exceptionDetails) throw new Error(JSON.stringify(result.exceptionDetails));
    return result.result.value;
  };
  await command('Page.enable'); await command('Network.enable');
  await command('Browser.setDownloadBehavior', { behavior: 'allow', downloadPath: output });
  await command('Page.addScriptToEvaluateOnNewDocument', { source: `
    window.__cspViolations = [];
    document.addEventListener('securitypolicyviolation', event => window.__cspViolations.push({
      directive: event.effectiveDirective, blocked: event.blockedURI, disposition: event.disposition
    }));
  ` });
  await command('Page.navigate', { url: `${origin}/id-card/${id}` });
  await until(() => evaluate("document.querySelector('.pass-verified-badge')?.naturalWidth > 0 && [...document.querySelectorAll('.pass-signatures img')].length === 3 && [...document.querySelectorAll('.pass-signatures img')].every(x => x.naturalWidth > 0)"), 'approved card and blue signatures');
  assert.equal(await evaluate("document.querySelector('.member-pass--front .pass-id dd').textContent"), card.displayMemberId);
  assert.equal(await evaluate("document.querySelector('.member-pass--back').textContent.includes('Member ID')"), false);
  assert.ok(await evaluate("document.querySelector('.pass-verified-badge').getBoundingClientRect().bottom < document.querySelector('.pass-name').getBoundingClientRect().top"));
  await command('Network.setBlockedURLs', { urls: ['*jspdf*'] });
  await evaluate("[...document.querySelectorAll('button')].find(x => x.textContent.trim() === 'Download ID Card').click()");
  const png = await until(async () => (await readdir(output)).find(name => name.endsWith('.png')), 'PNG download');
  assert.equal((await readFile(path.join(output, png))).subarray(1, 4).toString(), 'PNG');
  const beforeReload = await evaluate('window.__cspViolations');
  await command('Network.setBlockedURLs', { urls: [] });
  await command('Page.reload');
  await until(() => evaluate("[...document.querySelectorAll('button')].some(x => x.textContent.trim() === 'Download ID Card' && !x.disabled)"), 'card reload');
  await evaluate("[...document.querySelectorAll('button')].find(x => x.textContent.trim() === 'Download ID Card').click()");
  const pdf = await until(async () => (await readdir(output)).find(name => name.endsWith('.pdf')), 'PDF download');
  assert.ok((await readFile(path.join(output, pdf))).subarray(0, 5).toString() === '%PDF-');
  const afterCard = await evaluate('window.__cspViolations');
  await command('Page.navigate', { url: `${origin}/register` });
  await until(() => evaluate("document.querySelector('.form-panel form input[autocomplete=name]') !== null"), 'registration image-check page');
  await evaluate(`(() => {
    const set = Object.getOwnPropertyDescriptor(HTMLInputElement.prototype, 'value').set;
    const inputs = document.querySelectorAll('.form-panel form input');
    set.call(inputs[0], 'Synthetic OCR Member'); inputs[0].dispatchEvent(new Event('input', { bubbles: true }));
    set.call(inputs[1], '9876543210'); inputs[1].dispatchEvent(new Event('input', { bubbles: true }));
  })()`);
  await evaluate("document.querySelector('.form-panel form').requestSubmit()");
  await until(() => evaluate("document.querySelector('input[name=aadhaarNumber]') !== null"), 'mandatory Aadhaar number input');
  assert.ok(await evaluate(`(() => {
    const field = document.querySelector('input[name=aadhaarNumber]');
    const star = field.closest('label').querySelector('.required-star');
    return !document.querySelector('input[name=aadhaarPhoto]') && field.required &&
      field.inputMode === 'numeric' && field.maxLength === 12 && star?.textContent === '*' &&
      getComputedStyle(star).color === 'rgb(176, 0, 32)';
  })()`), 'red required star and numeric input replace Aadhaar image upload');
  await evaluate(`(async () => {
    const image = await createImageBitmap(await (await fetch(${JSON.stringify(apiBase.replace(/\/api$/, '') + '/synthetic-passport.png')})).blob());
    const canvas = document.createElement('canvas'); canvas.width = 600; canvas.height = 750;
    const context = canvas.getContext('2d'); context.drawImage(image, 0, 0, 600, 750);
    const blob = await new Promise(resolve => canvas.toBlob(resolve, 'image/jpeg', 0.9));
    const input = document.querySelector('input[name=profilePhoto]');
    const transfer = new DataTransfer(); transfer.items.add(new File([blob], 'synthetic-passport.jpg', { type: 'image/jpeg' }));
    input.files = transfer.files; input.dispatchEvent(new Event('change', { bubbles: true }));
  })()`);
  await until(() => evaluate("document.querySelector('img[alt=\"Selected passport preview\"]') !== null && !document.querySelector('.form-panel .notice.error')"), 'synthetic single-face portrait accepted for admin review', 30000);
  await evaluate(`(async () => {
    const image = await createImageBitmap(await (await fetch(${JSON.stringify(apiBase.replace(/\/api$/, '') + '/synthetic-passport.png')})).blob());
    const canvas = document.createElement('canvas'); canvas.width = 600; canvas.height = 750;
    const context = canvas.getContext('2d'); context.fillStyle = '#eee'; context.fillRect(0, 0, 600, 750);
    context.drawImage(image, 0, 90, 300, 375); context.drawImage(image, 300, 90, 300, 375);
    const blob = await new Promise(resolve => canvas.toBlob(resolve, 'image/jpeg', 0.9));
    const input = document.querySelector('input[name=profilePhoto]');
    const transfer = new DataTransfer(); transfer.items.add(new File([blob], 'synthetic-group.jpg', { type: 'image/jpeg' }));
    input.files = transfer.files; input.dispatchEvent(new Event('change', { bubbles: true }));
  })()`);
  await until(() => evaluate("document.querySelector('.form-panel .notice.error')?.textContent.includes('More than one face')"), 'local face model rejects synthetic group portrait', 30000);
  await evaluate(`(async () => {
    const image = await createImageBitmap(await (await fetch(${JSON.stringify(apiBase.replace(/\/api$/, '') + '/synthetic-passport.png')})).blob());
    const canvas = document.createElement('canvas'); canvas.width = 600; canvas.height = 750;
    const context = canvas.getContext('2d'); context.fillStyle = '#ddd'; context.fillRect(0, 0, 600, 750);
    context.drawImage(image, 200, 145, 200, 250);
    const blob = await new Promise(resolve => canvas.toBlob(resolve, 'image/jpeg', 0.9));
    const input = document.querySelector('input[name=profilePhoto]');
    const transfer = new DataTransfer(); transfer.items.add(new File([blob], 'synthetic-full-body-framing.jpg', { type: 'image/jpeg' }));
    input.files = transfer.files; input.dispatchEvent(new Event('change', { bubbles: true }));
  })()`);
  await until(() => evaluate("document.querySelector('.form-panel .notice.error')?.textContent.includes('too small') || [...document.querySelectorAll('.registration-form small[role=status]')].some(x => x.textContent.includes('A face was not detected reliably'))"), 'full-body style framing gets a crop or admin-review warning', 30000);
  await evaluate(`(async () => {
    const image = await createImageBitmap(await (await fetch(${JSON.stringify(apiBase.replace(/\/api$/, '') + '/synthetic-passport.png')})).blob());
    const canvas = document.createElement('canvas'); canvas.width = 600; canvas.height = 750;
    const context = canvas.getContext('2d'); context.fillStyle = '#ddd'; context.fillRect(0, 0, 600, 750);
    context.filter = 'blur(70px)'; context.drawImage(image, 0, 0, 600, 750);
    const blob = await new Promise(resolve => canvas.toBlob(resolve, 'image/jpeg', 0.9));
    const input = document.querySelector('input[name=profilePhoto]');
    const transfer = new DataTransfer(); transfer.items.add(new File([blob], 'synthetic-blurred.jpg', { type: 'image/jpeg' }));
    input.files = transfer.files; input.dispatchEvent(new Event('change', { bubbles: true }));
  })()`);
  await until(() => evaluate("document.querySelector('.form-panel .notice.error')?.textContent.includes('blurred') || [...document.querySelectorAll('.registration-form small[role=status]')].some(x => x.textContent.includes('blurred'))"), 'blurred portrait rejected or warned', 30000);
  await evaluate(`(async () => {
    const image = await createImageBitmap(await (await fetch(${JSON.stringify(apiBase.replace(/\/api$/, '') + '/synthetic-passport.png')})).blob());
    const canvas = document.createElement('canvas'); canvas.width = 600; canvas.height = 750;
    const context = canvas.getContext('2d'); context.fillStyle = '#ff00ff'; context.fillRect(0, 0, 600, 750);
    context.filter = 'saturate(8)'; context.drawImage(image, 40, 0, 520, 750);
    const blob = await new Promise(resolve => canvas.toBlob(resolve, 'image/jpeg', 0.9));
    const input = document.querySelector('input[name=profilePhoto]');
    const transfer = new DataTransfer(); transfer.items.add(new File([blob], 'synthetic-filtered.jpg', { type: 'image/jpeg' }));
    input.files = transfer.files; input.dispatchEvent(new Event('change', { bubbles: true }));
  })()`);
  try {
    await until(() => evaluate("[...document.querySelectorAll('.registration-form small[role=status]')].some(x => x.textContent.includes('Heavy color effects'))"), 'heavy color filter warned', 12000);
  } catch (error) {
    console.error('Filtered portrait diagnostic:', JSON.stringify({
      warnings: await evaluate("[...document.querySelectorAll('.registration-form small[role=status]')].map(x => x.textContent)"),
      error: await evaluate("document.querySelector('.form-panel .notice.error')?.textContent || ''")
    }));
    throw error;
  }
  await evaluate(`(() => {
    const input = document.querySelector('input[name=profilePhoto]');
    const transfer = new DataTransfer(); transfer.items.add(new File(['<html>not an image</html>'], 'fake.png', { type: 'image/png' }));
    input.files = transfer.files; input.dispatchEvent(new Event('change', { bubbles: true }));
  })()`);
  await until(() => evaluate("document.querySelector('.form-panel .notice.error')?.textContent.includes('contents do not match')"), 'wrong-file upload rejected');
  assert.ok(imageRequests.some(url => url.includes('/vision/blaze_face_short_range.tflite')), 'local face model loaded');
  assert.ok(existsSync(path.join(dist, 'ocr', 'eng.traineddata.gz')), 'OCR language model is packaged locally');
  assert.ok(imageRequests.every(url => url.startsWith(origin + '/')), 'face model loads only from this site');
  const violations = [...beforeReload, ...afterCard, ...await evaluate('window.__cspViolations')];
  const counts = Object.entries(violations.reduce((acc, item) => {
    const key = `${item.directive} ${item.blocked} ${item.disposition}`;
    acc[key] = (acc[key] || 0) + 1; return acc;
  }, {}));
  console.log(`Card preview, PNG, PDF and Aadhaar number UI: PASS; CSP reports: ${JSON.stringify(counts)}`);
  if (process.env.WTS_CSP_ENFORCE === '1') assert.equal(violations.length, 0, 'enforced policy has no card violations');
} finally {
  socket?.close(); browser.kill();
  await new Promise(resolve => frontend.close(resolve));
  await new Promise(resolve => mock.close(resolve));
}
