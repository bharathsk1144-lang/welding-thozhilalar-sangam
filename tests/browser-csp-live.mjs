import assert from 'node:assert/strict';
import { spawn } from 'node:child_process';
import { existsSync } from 'node:fs';
import { mkdir, readFile } from 'node:fs/promises';
import path from 'node:path';

// Read-only public smoke test. No account credentials, registration, or production writes.
const base = process.env.WTS_CSP_TEST_URL || 'https://www.weldingthozhilalarsangam.com';
const profile = path.join(process.cwd(), 'artifacts', `csp-live-${Date.now()}`);
await mkdir(profile, { recursive: true });
const binary = process.env.WTS_TEST_BROWSER || [
  'C:/Program Files/Google/Chrome/Application/chrome.exe',
  'C:/Program Files (x86)/Microsoft/Edge/Application/msedge.exe'
].find(existsSync);
assert.ok(binary, 'Chrome or Edge is required');
const browser = spawn(binary, [
  '--headless=new', '--disable-gpu', '--no-first-run', '--no-default-browser-check',
  '--disable-background-networking', '--remote-debugging-port=0', `--user-data-dir=${profile}`, 'about:blank'
], { windowsHide: true, stdio: 'ignore' });
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
  let id = 0;
  const pending = new Map();
  socket.onmessage = ({ data }) => {
    const result = JSON.parse(data);
    if (!result.id || !pending.has(result.id)) return;
    const item = pending.get(result.id);
    pending.delete(result.id);
    clearTimeout(item.timer);
    result.error ? item.reject(new Error(result.error.message)) : item.resolve(result.result);
  };
  const command = (method, params = {}) => new Promise((resolve, reject) => {
    const key = ++id;
    const timer = setTimeout(() => { pending.delete(key); reject(new Error(`Timed out: ${method}`)); }, 45000);
    pending.set(key, { resolve, reject, timer });
    socket.send(JSON.stringify({ id: key, method, params }));
  });
  const evaluate = async expression => {
    const result = await command('Runtime.evaluate', { expression, awaitPromise: true, returnByValue: true });
    if (result.exceptionDetails) throw new Error(JSON.stringify(result.exceptionDetails));
    return result.result.value;
  };
  await command('Page.enable');
  await command('Runtime.enable');
  await command('Page.addScriptToEvaluateOnNewDocument', { source: `
    window.__cspViolations = [];
    document.addEventListener('securitypolicyviolation', event => {
      window.__cspViolations.push({ directive: event.effectiveDirective, blocked: event.blockedURI,
        disposition: event.disposition, sample: event.sample });
    });
  ` });
  const routes = [
    ['/', '.sangam-hero'], ['/about', '.tamil-document__body'],
    ['/register', '.form-panel form'], ['/user-login', 'input[type=password]'],
    ['/admin/login', 'input[type=password]'], ['/verify-member', 'input']
  ];
  const all = [];
  const origins = new Set();
  for (const [route, selector] of routes) {
    await command('Page.navigate', { url: `${base}${route}` });
    await until(() => evaluate(`document.querySelector(${JSON.stringify(selector)}) !== null`), route);
    if (route === '/register') {
      await evaluate(`(() => {
        const fields = document.querySelectorAll('.form-panel input');
        const setter = Object.getOwnPropertyDescriptor(HTMLInputElement.prototype, 'value').set;
        for (const [field, value] of [[fields[0], 'Synthetic CSP Viewer'], [fields[1], '9999999999']]) {
          setter.call(field, value); field.dispatchEvent(new Event('input', { bubbles: true }));
        }
        document.querySelector('.form-panel form').requestSubmit();
      })()`);
      await until(() => evaluate("document.querySelector('.registration-form .tamil-document') !== null"), 'registration rules');
      await until(() => evaluate("document.querySelectorAll('select[name=district] option').length > 1"), 'district choices load');
      assert.equal(await evaluate("document.querySelectorAll('.consent-box .tamil-document h3').length"), 10, 'ten Tamil rules render');
      assert.equal(await evaluate("document.querySelector('input[name=consentAccepted]').checked"), false, 'consent starts unchecked');
    }
    await pause(700);
    const result = await evaluate(`({ violations: window.__cspViolations,
      origins: [...new Set(performance.getEntriesByType('resource').map(x => new URL(x.name).origin))],
      horizontalOverflow: document.documentElement.scrollWidth > innerWidth })`);
    all.push(...result.violations.map(item => ({ route, ...item })));
    result.origins.forEach(origin => origins.add(origin));
    assert.equal(result.horizontalOverflow, false, `horizontal overflow on ${route}`);
    console.log(`${route}: ${result.violations.length} CSP reports; resources ${result.origins.join(', ')}`);
  }
  await command('Emulation.setDeviceMetricsOverride', { width: 390, height: 844, deviceScaleFactor: 1, mobile: true });
  await command('Page.navigate', { url: `${base}/register` });
  await until(() => evaluate("document.querySelector('.form-panel form') !== null"), 'mobile registration');
  assert.equal(await evaluate('document.documentElement.scrollWidth > innerWidth'), false, 'mobile horizontal overflow');
  all.push(...(await evaluate('window.__cspViolations')).map(item => ({ route: '/register mobile', ...item })));
  console.log(`Resource origins: ${[...origins].join(', ')}`);
  console.log(`CSP reports: ${JSON.stringify([...new Map(all.map(item => [JSON.stringify(item), item])).values()])}`);
  if (process.env.WTS_CSP_EXPECT_ENFORCED === '1') {
    assert.equal(all.length, 0, 'enforced CSP has no browser violations');
  }
} finally {
  socket?.close();
  browser.kill();
}
