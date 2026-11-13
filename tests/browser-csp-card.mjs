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
    '.css': 'text/css', '.png': 'image/png', '.svg': 'image/svg+xml' })[ext] || 'application/octet-stream');
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
  assert.equal(await evaluate("document.querySelector('.pass-display-id dd').textContent"), card.displayMemberId);
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
  const violations = [...beforeReload, ...await evaluate('window.__cspViolations')];
  const counts = Object.entries(violations.reduce((acc, item) => {
    const key = `${item.directive} ${item.blocked} ${item.disposition}`;
    acc[key] = (acc[key] || 0) + 1; return acc;
  }, {}));
  console.log(`Card preview, PNG and PDF: PASS; CSP reports: ${JSON.stringify(counts)}`);
  if (process.env.WTS_CSP_ENFORCE === '1') assert.equal(violations.length, 0, 'enforced policy has no card violations');
} finally {
  socket?.close(); browser.kill();
  await new Promise(resolve => frontend.close(resolve));
  await new Promise(resolve => mock.close(resolve));
}
