import assert from 'node:assert/strict';
import { spawn } from 'node:child_process';
import { createServer } from 'node:net';
import { existsSync, readFileSync } from 'node:fs';
import { mkdir, readFile, readdir } from 'node:fs/promises';
import path from 'node:path';

// Called by SecurityRegression with a random, isolated MongoDB database and
// synthetic member. Never targets a public API or a production record.
const fixture = JSON.parse(readFileSync(process.argv[2], 'utf8'));
assert.match(fixture.runRoot, /WTS_SecurityTests_|security-tests/i);
assert.match(fixture.apiUrl, /^http:\/\/127\.0\.0\.1:\d+$/);
const output = path.join(fixture.runRoot, 'admin-edit-browser');
await mkdir(output, { recursive: true });
const delay = (ms) => new Promise(resolve => setTimeout(resolve, ms));
async function until(action, label, timeout = 45000) {
  const end = Date.now() + timeout;
  while (Date.now() < end) {
    try { const value = await action(); if (value) return value; } catch { /* wait for page state */ }
    await delay(100);
  }
  throw new Error(`Timed out: ${label}`);
}
let vite, chrome, socket;
try {
  let port;
  for (const candidate of [5173, 5174, 5175]) {
    const probe = createServer();
    try {
      await new Promise((resolve, reject) => {
        probe.once('error', reject);
        probe.listen(candidate, '127.0.0.1', resolve);
      });
      port = candidate;
      await new Promise(resolve => probe.close(resolve));
      break;
    } catch { probe.close(); }
  }
  assert.ok(port, 'an allowed development Vite port is available');
  vite = spawn(process.execPath, [path.join(fixture.root, 'client/node_modules/vite/bin/vite.js'),
    '--host', '127.0.0.1', '--port', String(port), '--strictPort'], {
    cwd: path.join(fixture.root, 'client'), windowsHide: true, stdio: 'ignore',
    env: { ...process.env, VITE_API_BASE_URL: `${fixture.apiUrl}/api` }
  });
  const origin = `http://127.0.0.1:${port}`;
  await until(async () => vite.exitCode === null && (await fetch(origin)).ok, 'isolated Vite');
  const binary = process.env.WTS_TEST_BROWSER || [
    'C:/Program Files/Google/Chrome/Application/chrome.exe',
    'C:/Program Files (x86)/Microsoft/Edge/Application/msedge.exe'
  ].find(existsSync);
  assert.ok(binary, 'Chrome or Edge required');
  const profile = path.join(output, 'chrome-profile');
  await mkdir(profile);
  chrome = spawn(binary, ['--headless=new', '--disable-gpu', '--no-first-run',
    '--no-default-browser-check', '--disable-background-networking',
    '--remote-debugging-port=0', `--user-data-dir=${profile}`, 'about:blank'],
  { windowsHide: true, stdio: 'ignore' });
  const debugPort = await until(async () => (await readFile(path.join(profile, 'DevToolsActivePort'), 'utf8')).split('\n')[0], 'browser');
  const target = await (await fetch(`http://127.0.0.1:${debugPort}/json/new?about:blank`, { method: 'PUT' })).json();
  socket = new WebSocket(target.webSocketDebuggerUrl);
  await new Promise((resolve, reject) => { socket.onopen = resolve; socket.onerror = reject; });
  let sequence = 0;
  const pending = new Map();
  socket.onmessage = ({ data }) => {
    const message = JSON.parse(data);
    if (!message.id || !pending.has(message.id)) return;
    const item = pending.get(message.id); pending.delete(message.id); clearTimeout(item.timer);
    message.error ? item.reject(new Error(message.error.message)) : item.resolve(message.result);
  };
  const command = (method, params = {}) => new Promise((resolve, reject) => {
    const id = ++sequence;
    const timer = setTimeout(() => { pending.delete(id); reject(new Error(`Browser timeout: ${method}`)); }, 45000);
    pending.set(id, { resolve, reject, timer });
    socket.send(JSON.stringify({ id, method, params }));
  });
  const evaluate = async expression => {
    const result = await command('Runtime.evaluate', { expression, awaitPromise: true, returnByValue: true });
    if (result.exceptionDetails) throw new Error(JSON.stringify(result.exceptionDetails));
    return result.result.value;
  };
  await command('Page.enable'); await command('Network.enable');
  await command('Browser.setDownloadBehavior', { behavior: 'allow', downloadPath: output });
  await command('Page.navigate', { url: origin });
  await until(() => evaluate('document.readyState === "complete"'), 'frontend load');
  await evaluate(`localStorage.setItem('adminToken', ${JSON.stringify(fixture.adminToken)})`);
  await command('Page.navigate', { url: `${origin}/admin/dashboard` });
  const memberLink = `a[href="/admin/users/${fixture.userId}"]`;
  await until(() => evaluate(`document.querySelector(${JSON.stringify(memberLink)}) !== null`), 'synthetic member in Admin Users');
  await evaluate(`document.querySelector(${JSON.stringify(memberLink)}).click()`);
  await until(() => evaluate("document.querySelector('.details-panel h1')?.textContent === 'Corrected synthetic member'"), 'member View');
  await evaluate("[...document.querySelectorAll('.detail-actions button')].find(button => button.textContent.trim() === 'Edit').click()");
  await until(() => evaluate("document.querySelector('.registration-form input[name=name]') !== null"), 'prepopulated Edit form');
  assert.equal(await evaluate("document.querySelector('.registration-form input[name=name]').value"), 'Corrected synthetic member');
  assert.equal(await evaluate("document.querySelector('.registration-form input[name=phoneNumber]').value"), '9876543260');
  assert.equal(await evaluate("document.querySelector('.registration-form select[name=district]').value"), 'Madurai');
  assert.ok(await evaluate("[...document.querySelectorAll('.registration-form button')].some(button => button.textContent.trim() === 'Cancel')"));
  await evaluate(`(() => {
    const values = { name: 'Browser Edited Synthetic Member', phoneNumber: '9876543261',
      fatherName: 'Browser Corrected Father', permanentAddress: 'Browser synthetic address, Coimbatore 600003',
      pincode: '600003' };
    const setter = Object.getOwnPropertyDescriptor(HTMLInputElement.prototype, 'value').set;
    for (const [name, value] of Object.entries(values)) {
      const field = document.querySelector('.registration-form [name=' + name + ']');
      if (field.tagName === 'TEXTAREA') {
        Object.getOwnPropertyDescriptor(HTMLTextAreaElement.prototype, 'value').set.call(field, value);
      } else setter.call(field, value);
      field.dispatchEvent(new Event('input', { bubbles: true }));
    }
    const district = document.querySelector('.registration-form select[name=district]');
    district.value = 'Coimbatore'; district.dispatchEvent(new Event('change', { bubbles: true }));
  })()`);
  assert.equal(await evaluate("document.querySelector('.registration-form select[name=district]').value"), 'Coimbatore');
  await evaluate("document.querySelector('.registration-form').requestSubmit()");
  await until(() => evaluate("document.querySelector('.details-panel h1')?.textContent === 'Browser Edited Synthetic Member' && document.querySelector('.notice.success')?.textContent.includes('saved successfully')"), 'saved member View');
  await until(() => evaluate("document.querySelector('.admin-id-preview .pass-name dd')?.textContent === 'Browser Edited Synthetic Member'"), 'refreshed card preview');
  assert.equal(await evaluate("document.querySelector('.admin-id-preview .pass-front-fields .pass-field:nth-child(3) dd').textContent"), 'Coimbatore');
  assert.equal(await evaluate("document.querySelector('.admin-id-preview .pass-back-fields .pass-field:nth-child(3) dd').textContent"), '9876543261');
  console.log('PASS admin Users View Edit Save and refreshed details/card preview');

  await command('Network.setBlockedURLs', { urls: ['*jspdf*'] });
  await evaluate("[...document.querySelectorAll('.detail-actions button')].find(button => button.textContent.trim() === 'Download ID Card').click()");
  const pngName = await until(async () => (await readdir(output)).find(name => name.endsWith('.png')), 'PNG fallback');
  const png = await readFile(path.join(output, pngName));
  assert.equal(png.subarray(1, 4).toString(), 'PNG');
  assert.equal(png.readUInt32BE(16), 1392, 'PNG has both card sides');
  console.log('PASS updated card PNG export');

  await command('Network.setBlockedURLs', { urls: [] });
  await until(() => evaluate("[...document.querySelectorAll('.detail-actions button')].some(button => button.textContent.trim() === 'Download ID Card' && !button.disabled)"), 'PNG export settled');
  await command('Page.reload');
  await until(() => evaluate("document.querySelector('.admin-id-preview .pass-name dd')?.textContent === 'Browser Edited Synthetic Member'"), 'updated card reload');
  await evaluate("[...document.querySelectorAll('.detail-actions button')].find(button => button.textContent.trim() === 'Download ID Card').click()");
  const pdfName = await until(async () => (await readdir(output)).find(name => name.endsWith('.pdf')), 'PDF download');
  const pdf = await readFile(path.join(output, pdfName));
  assert.equal(pdf.subarray(0, 4).toString(), '%PDF');
  assert.equal((pdf.toString('latin1').match(/\/Type \/Page\b/g) || []).length, 2, 'PDF contains both card sides');
  console.log('PASS updated card two-page PDF export');

  await evaluate("document.querySelector('.back-link').click()");
  await until(() => evaluate(`document.querySelector(${JSON.stringify(memberLink)}) !== null`), 'refreshed Admin User List');
  assert.equal(await evaluate(`document.querySelector(${JSON.stringify(memberLink)}).closest('tr').querySelector('td').textContent`), 'Browser Edited Synthetic Member');
  await evaluate("localStorage.removeItem('adminToken'); sessionStorage.setItem('wtsUserToken', 'member-only')");
  await command('Page.navigate', { url: `${origin}/admin/users/${fixture.otherId}` });
  await until(() => evaluate("location.pathname === '/admin/login'"), 'member blocked from Edit UI');
  console.log('PASS refreshed Admin User List and member denied Edit UI');
} finally {
  socket?.close(); chrome?.kill(); vite?.kill();
}
