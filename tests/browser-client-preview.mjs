import assert from 'node:assert/strict';
import { spawn } from 'node:child_process';
import { existsSync } from 'node:fs';
import { mkdir, readFile, writeFile } from 'node:fs/promises';
import path from 'node:path';

const origin = process.argv[2] || 'http://127.0.0.1:4173';
const demo = process.argv.includes('--demo');
const address = new URL(origin);
assert.ok(['127.0.0.1', 'localhost'].includes(address.hostname) || (address.protocol === 'https:' && address.hostname.endsWith('.trycloudflare.com')));
const runRoot = path.resolve('artifacts', `client-preview-browser-${Date.now()}`);
await mkdir(runRoot, { recursive: true });
const delay = ms => new Promise(resolve => setTimeout(resolve, ms));
async function until(action, label) {
  const end = Date.now() + 30000;
  while (Date.now() < end) { try { const result = await action(); if (result) return result; } catch { } await delay(100); }
  throw new Error(`Timed out: ${label}`);
}
const binary = ['C:/Program Files/Google/Chrome/Application/chrome.exe', 'C:/Program Files (x86)/Microsoft/Edge/Application/msedge.exe'].find(existsSync);
assert.ok(binary, 'Chrome/Edge required');
let chrome, socket;
const requests = [], errors = [];
try {
  const profile = path.join(runRoot, 'chrome-profile'); await mkdir(profile);
  chrome = spawn(binary, ['--headless=new', '--disable-gpu', '--no-first-run', '--no-default-browser-check', '--disable-background-networking',
    '--remote-debugging-port=0', `--user-data-dir=${profile}`, 'about:blank'], { windowsHide: true, stdio: 'ignore' });
  const port = await until(async () => (await readFile(path.join(profile, 'DevToolsActivePort'), 'utf8')).split('\n')[0], 'browser');
  const target = await (await fetch(`http://127.0.0.1:${port}/json/new?about:blank`, { method: 'PUT' })).json();
  socket = new WebSocket(target.webSocketDebuggerUrl);
  await new Promise((resolve, reject) => { socket.onopen = resolve; socket.onerror = reject; });
  let sequence = 0; const pending = new Map();
  socket.onmessage = ({ data }) => {
    const message = JSON.parse(data);
    if (message.method === 'Network.requestWillBeSent') requests.push(message.params);
    if (message.method === 'Runtime.exceptionThrown') errors.push(message.params.exceptionDetails.text);
    if (!message.id || !pending.has(message.id)) return;
    const item = pending.get(message.id); pending.delete(message.id); clearTimeout(item.timer);
    message.error ? item.reject(new Error(message.error.message)) : item.resolve(message.result);
  };
  const command = (method, params = {}) => new Promise((resolve, reject) => {
    const id = ++sequence, timer = setTimeout(() => { pending.delete(id); reject(new Error(`Browser timeout: ${method}`)); }, 30000);
    pending.set(id, { resolve, reject, timer }); socket.send(JSON.stringify({ id, method, params }));
  });
  async function evaluate(expression) {
    const result = await command('Runtime.evaluate', { expression, awaitPromise: true, returnByValue: true });
    if (result.exceptionDetails) throw new Error('Preview evaluation failed');
    return result.result.value;
  }
  await command('Page.enable'); await command('Runtime.enable'); await command('Network.enable');
  await command('Page.navigate', { url: origin });
  try {
    await until(() => evaluate("!!document.querySelector('.sangam-hero') && !!document.querySelector('.client-preview-banner')"), 'preview homepage');
  } catch (error) {
    console.error(JSON.stringify({ errors, requests: requests.map(item => ({ type: item.type, url: item.request.url })) }));
    console.error(await evaluate('document.body.innerText'));
    throw error;
  }
  assert.ok(await evaluate("document.querySelector('.brand-logo').getAttribute('src') === '/wts-logo-client-transparent.png'"));
  assert.ok(await evaluate("!document.querySelector('[data-statistic]') && document.querySelector('.preview-statistics').textContent.includes('after launch')"));
  assert.equal(await evaluate("document.querySelectorAll('form,input,textarea,select').length"), 0);
  for (const width of [1440, 390, 320]) {
    await command('Emulation.setDeviceMetricsOverride', { width, height: 1000, deviceScaleFactor: 1, mobile: width < 600 });
    assert.ok(await evaluate('document.documentElement.scrollWidth <= innerWidth'), `preview fits ${width}px`);
    await until(() => evaluate("[...document.querySelectorAll('.brand-logo,.sangam-hero img')].every(img=>img.complete && img.naturalWidth > 0)"), 'preview logo');
    if (width === 390) {
      await evaluate("document.querySelector('.menu-toggle').click()");
      assert.equal(await evaluate("document.querySelector('.menu-toggle').getAttribute('aria-expanded')"), 'true');
      await command('Input.dispatchKeyEvent', { type: 'keyDown', key: 'Escape', code: 'Escape' });
      assert.equal(await evaluate("document.querySelector('.menu-toggle').getAttribute('aria-expanded')"), 'false');
    }
    const shot = await command('Page.captureScreenshot', { format: 'png' });
    await writeFile(path.join(runRoot, `homepage-${width}.png`), Buffer.from(shot.data, 'base64'));
  }
  if (demo) {
    await command('Page.navigate', { url: origin + '/register' });
    await until(() => evaluate("!!document.querySelector('.demo-registration form')"), 'demo registration');
    const fill = (name, value) => evaluate(`(() => { const field = document.querySelector('[name=${name}]'); Object.getOwnPropertyDescriptor(HTMLInputElement.prototype, 'value').set.call(field, ${JSON.stringify(value)}); field.dispatchEvent(new Event('input', { bubbles: true })); })()`);
    const submit = () => evaluate("document.querySelector('.demo-registration form').requestSubmit()");
    await fill('name', 'Synthetic Mobile Reviewer'); await fill('phone', 'invalid'); await submit();
    await until(() => evaluate("document.querySelector('[role=alert]')?.textContent.includes('valid sample')"), 'invalid mobile rejected');
    await fill('phone', '+919876543210'); await submit();
    await until(() => evaluate("!!document.querySelector('[name=district]')"), 'step two');
    assert.ok(await evaluate("!document.querySelector('[name=demoCode]') && !document.body.textContent.includes('OTP')"), 'demo registration does not show OTP');
    assert.ok(await evaluate("document.querySelector('[name=name]').readOnly && document.querySelector('[name=name]').value === 'Synthetic Mobile Reviewer' && document.querySelector('[name=phone]').readOnly && document.querySelector('[name=phone]').value === '9876543210'"));
    assert.equal(await evaluate("document.querySelectorAll('input[type=password],input[type=file]').length"), 0);
    await submit(); await until(() => evaluate("document.querySelector('[role=alert]')?.textContent.includes('Select a sample district')"), 'required fields rejected');
    for (const width of [320, 390]) {
      await command('Emulation.setDeviceMetricsOverride', { width, height: 1100, deviceScaleFactor: 1, mobile: true });
      assert.ok(await evaluate('document.documentElement.scrollWidth <= innerWidth'), `demo form fits ${width}px`);
    }
    await evaluate("(() => { const field = document.querySelector('[name=district]'); field.value = 'Chennai'; field.dispatchEvent(new Event('change', { bubbles: true })); document.querySelector('.demo-ack input').click(); })()");
    await submit();
    await until(() => evaluate("!!document.querySelector('.demo-success') && document.querySelectorAll('[data-card-side]').length === 2"), 'demo success and synthetic card');
    assert.ok(await evaluate("document.body.textContent.includes('DEMO-NOT-VALID') && !document.body.textContent.includes('Synthetic Mobile Reviewer') && document.querySelectorAll('.pass-contact-icon').length === 4"));
    for (const width of [1440, 390, 320]) {
      await command('Emulation.setDeviceMetricsOverride', { width, height: 1100, deviceScaleFactor: 1, mobile: width < 600 });
      assert.ok(await evaluate('document.documentElement.scrollWidth <= innerWidth'), `demo card fits ${width}px`);
      const shot = await command('Page.captureScreenshot', { format: 'png', captureBeyondViewport: true });
      await writeFile(path.join(runRoot, `demo-card-${width}.png`), Buffer.from(shot.data, 'base64'));
    }
    assert.ok(await evaluate('localStorage.length === 0 && sessionStorage.length === 0'), 'no persisted data or sessions');
    await command('Page.navigate', { url: origin + '/register' });
    await until(() => evaluate("document.querySelector('[name=name]')?.value === 'Demo Member' && !document.querySelector('[name=district]')"), 'reload clears demo state');
  }
  for (const route of [...(demo ? [] : ['/register']), '/user-login', '/forgot-password', '/verify-member', '/my-id-card', '/id-card/not-a-member', '/admin/login', '/admin/dashboard', '/admin/users/not-a-member', '/payment/not-a-member']) {
    await command('Page.navigate', { url: origin + route });
    await until(() => evaluate("document.querySelector('.preview-unavailable h1')?.textContent.includes('Preview Mode')"), route);
    assert.equal(await evaluate("document.querySelectorAll('form,input,textarea,select,[type=submit]').length"), 0, `${route} cannot submit`);
    if (route === '/register') assert.equal(await evaluate("document.querySelector('h1').textContent"), 'Preview Mode — Registration will be available after launch');
  }
  assert.deepEqual(errors, [], 'no JavaScript runtime errors');
  assert.ok(requests.every(item => !['Fetch', 'XHR', 'WebSocket'].includes(item.type)), 'no API/session/socket requests');
  // Chrome draws its native date picker using an embedded data SVG; it makes no
  // network connection. Every actual network request must remain same-origin.
  assert.ok(requests.every(item => new URL(item.request.url).origin === address.origin || (item.type === 'Image' && item.request.url.startsWith('data:image/svg+xml;'))), 'only static same-origin network requests');
  assert.ok(requests.every(item => !/\/api\/|\/uploads\/|:5087|:27017/.test(item.request.url)), 'no backend/private-document requests');
  const report = { passed: true, origin, mobileWidths: [320, 390], demoRegistration: demo, protectedRoutesDisabled: demo ? 9 : 10, apiRequests: 0, runtimeErrors: 0, artifacts: runRoot };
  await writeFile(path.join(runRoot, 'report.json'), JSON.stringify(report, null, 2));
  console.log(JSON.stringify(report));
} finally { socket?.close(); chrome?.kill(); }
