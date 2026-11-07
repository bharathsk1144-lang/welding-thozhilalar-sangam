import assert from 'node:assert/strict';
import { spawn } from 'node:child_process';
import { createServer } from 'node:http';
import { existsSync } from 'node:fs';
import { mkdir, readFile, readdir, writeFile } from 'node:fs/promises';
import path from 'node:path';
import { inflateSync } from 'node:zlib';

// Frontend-only export regression. The mock API has synthetic GET fixtures and no database.
const root = process.cwd();
const runRoot = path.join(root, 'artifacts', `card-social-export-${Date.now()}`);
await mkdir(runRoot, { recursive: true });
const card = { id: '000000000000000000000001', name: 'Synthetic Export Member', memberId: 'WTS-2026-000001',
  displayMemberId: 'WTS-TEST-REG-000001', district: 'Chennai', phoneNumber: '9876543200', dateOfBirth: '1990-01-01',
  fatherName: 'Synthetic Father', bloodGroup: 'O+', issueDate: '2026-10-21', validityYear: '2027',
  permanentAddress: 'Synthetic house address, Test Street, Chennai 600001', status: 'Active Member', verificationStatus: 'Verified' };
let unexpectedRequests = 0;
let registrationOtpEnabled = false;
const mock = createServer((request, response) => {
  response.setHeader('Access-Control-Allow-Origin', '*');
  response.setHeader('Access-Control-Allow-Headers', 'Authorization, Content-Type');
  if (request.method === 'OPTIONS') { response.writeHead(204); response.end(); return; }
  if (request.method === 'GET' && request.url === '/api/districts') {
    response.setHeader('Content-Type', 'application/json'); response.end('[]'); return;
  }
  if (request.method === 'GET' && request.url === '/api/auth/registration-options') {
    response.setHeader('Content-Type', 'application/json'); response.end(JSON.stringify({ otpEnabled: registrationOtpEnabled })); return;
  }
  if (request.method !== 'GET' || request.url !== `/api/users/${card.id}/id-card`) {
    unexpectedRequests++; response.writeHead(405); response.end(); return;
  }
  response.setHeader('Content-Type', 'application/json'); response.end(JSON.stringify(card));
});
await new Promise(resolve => mock.listen(0, '127.0.0.1', resolve));
const apiBase = `http://127.0.0.1:${mock.address().port}/api`;
const delay = ms => new Promise(resolve => setTimeout(resolve, ms));
async function until(action, label, timeout = 45000) {
  const end = Date.now() + timeout;
  while (Date.now() < end) {
    try { const result = await action(); if (result) return result; } catch { }
    await delay(100);
  }
  throw new Error(`Timed out: ${label}`);
}

// Undo PNG row filters, also used by PDF's PNG predictor for embedded RGB images.
function rows(data, width, height, channels) {
  const stride = width * channels, output = Buffer.alloc(stride * height);
  assert.equal(data.length, (stride + 1) * height, 'raster scanline size');
  for (let y = 0; y < height; y++) {
    const type = data[y * (stride + 1)];
    for (let x = 0; x < stride; x++) {
      const a = x >= channels ? output[y * stride + x - channels] : 0;
      const b = y ? output[(y - 1) * stride + x] : 0;
      const c = y && x >= channels ? output[(y - 1) * stride + x - channels] : 0;
      const p = a + b - c, pa = Math.abs(p - a), pb = Math.abs(p - b), pc = Math.abs(p - c);
      const filter = [0, a, b, Math.floor((a + b) / 2), pa <= pb && pa <= pc ? a : pb <= pc ? b : c][type];
      assert.notEqual(filter, undefined, 'supported PNG predictor');
      output[y * stride + x] = (data[y * (stride + 1) + 1 + x] + filter) & 255;
    }
  }
  return { data: output, width, height, channels };
}
function pngRaster(buffer) {
  assert.equal(buffer.subarray(1, 4).toString(), 'PNG');
  const width = buffer.readUInt32BE(16), height = buffer.readUInt32BE(20);
  assert.equal(buffer[24], 8); assert.equal(buffer[28], 0, 'non-interlaced PNG');
  const channels = { 2: 3, 6: 4 }[buffer[25]], chunks = [];
  assert.ok(channels, 'RGB/RGBA export');
  for (let offset = 8; offset < buffer.length;) {
    const length = buffer.readUInt32BE(offset), type = buffer.toString('ascii', offset + 4, offset + 8);
    if (type === 'IDAT') chunks.push(buffer.subarray(offset + 8, offset + 8 + length));
    offset += length + 12;
  }
  return rows(inflateSync(Buffer.concat(chunks)), width, height, channels);
}
function pdfRasters(buffer) {
  const text = buffer.toString('latin1'), images = [];
  for (const match of text.matchAll(/\/Subtype \/Image\b/g)) {
    const start = match.index, stream = text.indexOf('stream', start), dictionary = text.slice(start, stream);
    if (!dictionary.includes('/DeviceRGB')) continue;
    const width = Number(dictionary.match(/\/Width (\d+)/)[1]), height = Number(dictionary.match(/\/Height (\d+)/)[1]);
    const length = Number(dictionary.match(/\/Length (\d+)/)[1]);
    let offset = stream + 6;
    if (buffer[offset] === 13) offset++; if (buffer[offset] === 10) offset++;
    let data = buffer.subarray(offset, offset + length);
    if (dictionary.includes('/FlateDecode')) data = inflateSync(data);
    if (data.length === width * height * 3) images.push({ data, width, height, channels: 3 });
    else images.push(rows(data, width, height, 3));
  }
  return images;
}
function assertIconPixels(raster, geometry, backOffset = 0) {
  for (const box of geometry.icons) {
    let maroon = 0;
    for (let y = Math.ceil(box.y * 2); y < Math.floor((box.y + box.height) * 2); y++) {
      for (let x = Math.ceil(box.x * 2) + backOffset; x < Math.floor((box.x + box.width) * 2) + backOffset; x++) {
        const index = (y * raster.width + x) * raster.channels;
        const [r, g, b] = raster.data.subarray(index, index + 3);
        if (r >= 70 && r < 150 && g < 60 && b < 90) maroon++;
      }
    }
    assert.ok(maroon >= 12, `${box.label} icon has visible maroon artwork in downloaded raster (${maroon} pixels)`);
  }
}
function assertContentPixels(rasters, geometry) {
  for (const box of geometry.content) {
    const raster = rasters[box.side];
    let ink = 0;
    for (let y = Math.ceil(box.y * 2); y < Math.floor((box.y + box.height) * 2); y++) {
      for (let x = Math.ceil(box.x * 2); x < Math.floor((box.x + box.width) * 2); x++) {
        const index = (y * raster.width + x) * raster.channels;
        const [r, g, b] = raster.data.subarray(index, index + 3);
        if (box.side === 0 ? r > 180 && g > 140 && b < 230 : r > 50 && r < 165 && g < 100 && b < 120) ink++;
      }
    }
    assert.ok(ink >= 20, `${box.label} remains visible in actual exported raster (${ink} pixels)`);
  }
}
let frontend, chrome, socket;
try {
  // Sentinels are layout fixtures only, never real/client addresses or live configuration.
  for (const scenario of [
    { mode: 'missing-addresses', office: '', president: '', approved: false },
    { mode: 'approved-long-addresses', office: 'OFFICE-ADDRESS-TEST-ONLY அலுவலக முகவரி '.repeat(8).trim(),
      president: 'PRESIDENT-ADDRESS-TEST-ONLY தலைவர் வீட்டு முகவரி '.repeat(8).trim(), approved: true },
    { mode: 'unapproved-address', office: 'OFFICE-ADDRESS-TEST-ONLY', president: 'PRESIDENT-NOT-APPROVED-TEST-ONLY', approved: false }
  ]) {
    const { mode } = scenario;
    registrationOtpEnabled = mode !== 'missing-addresses';
    const output = path.join(runRoot, mode); await mkdir(output);
    const instagram = '@welding_thozhilalargal_sangam';
    const facebook = 'வெல்டிங் தொழிலாளர்கள் சங்கம்';
    const portProbe = createServer();
    await new Promise(resolve => portProbe.listen(0, '127.0.0.1', resolve));
    const port = portProbe.address().port;
    await new Promise(resolve => portProbe.close(resolve));
    frontend = spawn(process.execPath, [path.join(root, 'client/node_modules/vite/bin/vite.js'), '--host', '127.0.0.1', '--port', String(port), '--strictPort'], {
      cwd: path.join(root, 'client'), windowsHide: true, stdio: 'ignore', env: { ...process.env,
        VITE_API_BASE_URL: apiBase, VITE_WTS_INSTAGRAM_ID: '', VITE_WTS_FACEBOOK_ID: '', VITE_WTS_YOUTUBE_URL: '',
        VITE_WTS_WEBSITE_URL: 'https://www.weldingthozhilalarsangam.com', VITE_WTS_OFFICE_ADDRESS: scenario.office,
        VITE_WTS_PRESIDENT_HOME_ADDRESS: scenario.president,
        VITE_WTS_PRESIDENT_HOME_ADDRESS_APPROVED_FOR_PUBLICATION: String(scenario.approved) }
    });
    const origin = `http://127.0.0.1:${port}`;
    await until(async () => { assert.equal(frontend.exitCode, null, 'isolated Vite is running'); return (await fetch(origin)).ok; }, 'isolated Vite');
    const binary = process.env.WTS_TEST_BROWSER || ['C:/Program Files/Google/Chrome/Application/chrome.exe', 'C:/Program Files (x86)/Microsoft/Edge/Application/msedge.exe'].find(existsSync);
    assert.ok(binary, 'Chrome or Edge required');
    const profile = path.join(output, 'chrome-profile'); await mkdir(profile);
    chrome = spawn(binary, ['--headless=new', '--disable-gpu', '--no-first-run', '--no-default-browser-check', '--disable-background-networking',
      '--remote-debugging-port=0', `--user-data-dir=${profile}`, 'about:blank'], { windowsHide: true, stdio: 'ignore' });
    const debugPort = await until(async () => (await readFile(path.join(profile, 'DevToolsActivePort'), 'utf8')).split('\n')[0], 'browser');
    const target = await (await fetch(`http://127.0.0.1:${debugPort}/json/new?about:blank`, { method: 'PUT' })).json();
    socket = new WebSocket(target.webSocketDebuggerUrl);
    await new Promise((resolve, reject) => { socket.onopen = resolve; socket.onerror = reject; });
    let sequence = 0; const pending = new Map();
    socket.onmessage = ({ data }) => {
      const message = JSON.parse(data);
      if (!message.id || !pending.has(message.id)) return;
      const item = pending.get(message.id); pending.delete(message.id); clearTimeout(item.timer);
      message.error ? item.reject(new Error(message.error.message)) : item.resolve(message.result);
    };
    const command = (method, params = {}) => new Promise((resolve, reject) => {
      const id = ++sequence, timer = setTimeout(() => { pending.delete(id); reject(new Error(`Browser timeout: ${method}`)); }, 45000);
      pending.set(id, { resolve, reject, timer }); socket.send(JSON.stringify({ id, method, params }));
    });
    const evaluate = async expression => {
      const result = await command('Runtime.evaluate', { expression, awaitPromise: true, returnByValue: true });
      if (result.exceptionDetails) throw new Error(JSON.stringify(result.exceptionDetails));
      return result.result.value;
    };
    await command('Page.enable'); await command('Network.enable');
    await command('Browser.setDownloadBehavior', { behavior: 'allow', downloadPath: output });
    await command('Page.addScriptToEvaluateOnNewDocument', { source: `(() => {
      const original = HTMLCanvasElement.prototype.toDataURL;
      HTMLCanvasElement.prototype.toDataURL = function(...args) {
        const back = document.querySelector('.card-pair--export [data-card-side=back]');
        if (back && this.width >= 680) {
          const bounds = back.getBoundingClientRect();
          window.__exportGeometry = { icons: [...back.querySelectorAll('.pass-contact-icon')].map(icon => {
            const r = icon.getBoundingClientRect(); return { label: icon.getAttribute('aria-label'), x:r.x-bounds.x, y:r.y-bounds.y, width:r.width, height:r.height };
          }), content: [
            [0, '.pass-brand__names', 'Official names'], [0, '.pass-office p', 'Front office address'],
            [0, '.pass-signatures__treasurer img', 'State Treasurer signature'],
            [1, '.pass-address dd', 'Member house address'],
            [1, '.pass-organization-contact dl > div:nth-child(2) dd', 'Tamil Facebook name'],
            [1, '.pass-organization-contact dl > div:nth-child(3) dd', 'YouTube URL'],
            [1, '.pass-organization-contact dl > div:nth-child(4) dd', 'Website URL']
          ].map(([side, selector, label]) => {
            const parent = document.querySelector('.card-pair--export [data-card-side=' + (side ? 'back' : 'front') + ']');
            const p = parent.getBoundingClientRect(), r = parent.querySelector(selector).getBoundingClientRect();
            return { side, label, x:r.x-p.x, y:r.y-p.y, width:r.width, height:r.height };
          }) };
        }
        return original.apply(this, args);
      };
    })();` });
    await command('Emulation.setDeviceMetricsOverride', { width: 1440, height: 1100, deviceScaleFactor: 1, mobile: false });
    await command('Page.navigate', { url: `${origin}/id-card/${card.id}` });
    await until(() => evaluate("document.querySelectorAll('.pass-contact-icon').length === 4"), 'card social icons');
    assert.deepEqual(await evaluate("[...document.querySelectorAll('.pass-organization-contact dd')].map(node => node.textContent)"), [instagram, facebook, 'youtube.com/@WeldingThozhilalargalSangam', 'www.weldingthozhilalarsangam.com']);
    assert.deepEqual(await evaluate("[...document.querySelectorAll('.pass-contact-icon')].map(svg => [svg.getAttribute('aria-label'), svg.getAttribute('role')])"), [['Instagram', 'img'], ['Facebook', 'img'], ['YouTube', 'img'], ['Website', 'img']]);
    assert.ok(await evaluate("!/(Instagram|Facebook|YouTube|Website):/.test(document.querySelector('.pass-organization-contact').textContent)"));
    assert.equal(await evaluate("document.querySelectorAll('.pass-organization-contact a').length"), 0, 'no invented social profile links');
    assert.equal(await evaluate("document.querySelector('.pass-address dd').textContent"), card.permanentAddress);
    assert.equal(await evaluate("document.querySelector('.member-pass--front .pass-office p').textContent"), scenario.office || '146 C1, VELAYUDHA NAGAR,\nT.PALUR MAIN ROAD,\nJAYANKONDAM,\nARIYALUR DISTRICT - 621802');
    assert.deepEqual(await evaluate("[...document.querySelectorAll('.pass-office > span')].map(node => node.textContent)"), ['7373605560', 'rksankar73@gmail.com']);
    assert.ok(await evaluate("document.querySelector('.pass-signatures__treasurer img').complete && document.querySelector('.pass-signatures__treasurer img').naturalWidth > 0"));
    assert.equal(await evaluate("document.querySelector('.pass-signatures__treasurer strong').textContent"), 'S.விக்னேஷ்');
    assert.deepEqual(await evaluate("[...document.querySelectorAll('.pass-signatures__treasurer small')].map(node => node.textContent)"), ['மாநில பொருளாளர்', 'State Treasurer']);
    assert.ok(await evaluate("!document.querySelector('.pass-president') && !document.querySelector('.membership-card').textContent.includes('President’s Home Address') && !document.querySelector('.membership-card').textContent.includes(\"President's Home Address\")"), 'President home address section is absent from both card sides');
    assert.ok(await evaluate("!document.querySelector('.member-pass--back .pass-office')"), 'no office address section remains on the back');
    if (!scenario.approved && scenario.president) assert.ok(await evaluate(`!document.querySelector('.membership-card').textContent.includes(${JSON.stringify(scenario.president)})`), 'unapproved President address is not rendered');
    assert.equal(await evaluate("document.querySelector('.pass-display-id dd').textContent"), card.displayMemberId);
    assert.ok(await evaluate("!document.querySelector('.membership-card').textContent.toLowerCase().includes('aadhaar')"));
    assert.equal(await evaluate("document.querySelector('.pass-brand__names > span').textContent"), 'Welding Thozhilalar Sangam');
    assert.equal(await evaluate("document.querySelector('.pass-brand__names strong').textContent"), facebook);
    assert.ok(await evaluate("document.querySelector('.pass-brand img').getAttribute('src') === '/wts-logo-client-transparent.png' && getComputedStyle(document.querySelector('.pass-brand img')).objectFit === 'contain'"));
    for (const width of [320, 390, 1440]) {
      await command('Emulation.setDeviceMetricsOverride', { width, height: 1100, deviceScaleFactor: 1, mobile: width < 600 });
      assert.ok(await evaluate(`(() => {
        const icons = [...document.querySelectorAll('.pass-contact-icon')].map(node => node.getBoundingClientRect());
        const logo = document.querySelector('.pass-brand img').getBoundingClientRect();
        const names = document.querySelector('.pass-brand__names').getBoundingClientRect();
        const identity = document.querySelector('.pass-brand__identity').getBoundingClientRect();
        const office = document.querySelector('.pass-office').getBoundingClientRect();
        const portrait = document.querySelector('.pass-portrait').getBoundingClientRect();
        return icons.every(r => r.width === 12 && r.height === 12 && Math.abs(r.x - icons[0].x) < 1) &&
          logo.right <= names.left && Math.abs((logo.top + logo.bottom) / 2 - (names.top + names.bottom) / 2) < 2 &&
          identity.bottom <= office.top && office.bottom < portrait.top && getComputedStyle(document.querySelector('.pass-office')).textAlign === 'center' &&
          [...document.querySelectorAll('.pass-organization-contact dd, .pass-office p, .pass-brand__names')].every(node => node.scrollWidth <= node.clientWidth && node.scrollHeight <= node.clientHeight + 1) && document.documentElement.scrollWidth <= innerWidth;
      })()`), `left logo/right names, centered office, aligned icons and wrapped values fit ${width}px`);
    }
    const screenshot = await command('Page.captureScreenshot', { format: 'png', captureBeyondViewport: true });
    await writeFile(path.join(output, 'preview.png'), Buffer.from(screenshot.data, 'base64'));
    await command('Network.setBlockedURLs', { urls: ['*jspdf*'] });
    await evaluate("[...document.querySelectorAll('button')].find(button => button.textContent.trim() === 'Download ID Card').click()");
    const pngName = await until(async () => (await readdir(output)).find(name => name.endsWith('.png') && name !== 'preview.png'), 'PNG fallback');
    const png = pngRaster(await readFile(path.join(output, pngName)));
    assert.equal(png.width, 1392, 'PNG contains both 680px sides plus gutter');
    assertIconPixels(png, await evaluate('window.__exportGeometry'), 712);
    // Present the combined PNG as two raster views for common front/back content checks.
    const front = { ...png, width: 680, data: Buffer.alloc(680 * png.height * png.channels) };
    const back = { ...front, data: Buffer.alloc(front.data.length) };
    for (let y = 0; y < png.height; y++) {
      png.data.copy(front.data, y * 680 * png.channels, y * png.width * png.channels, (y * png.width + 680) * png.channels);
      png.data.copy(back.data, y * 680 * png.channels, (y * png.width + 712) * png.channels, (y * png.width + 1392) * png.channels);
    }
    assertContentPixels([front, back], await evaluate('window.__exportGeometry'));
    console.log(`PASS ${mode}: front office address, no President home address, four social rows, Tamil, mobile layout and actual PNG content`);
    await command('Network.setBlockedURLs', { urls: [] });
    // Wait for the old download to settle before reload; otherwise readiness can
    // observe the old DOM and try to click its transient 'Preparing' button.
    await until(() => evaluate("[...document.querySelectorAll('button')].some(button => button.textContent.trim() === 'Download ID Card' && !button.disabled)"), 'PNG export settled');
    await evaluate('window.__cardReloadMarker = true');
    await command('Page.reload');
    await until(() => evaluate("!window.__cardReloadMarker && document.querySelectorAll('.pass-contact-icon').length === 4 && [...document.querySelectorAll('button')].some(button => button.textContent.trim() === 'Download ID Card' && !button.disabled)"), 'new document card reload');
    await evaluate("[...document.querySelectorAll('button')].find(button => button.textContent.trim() === 'Download ID Card').click()");
    const pdfName = await until(async () => (await readdir(output)).find(name => name.endsWith('.pdf')), 'PDF download');
    const pdf = await readFile(path.join(output, pdfName));
    assert.equal((pdf.toString('latin1').match(/\/Type \/Page\b/g) || []).length, 2, 'PDF contains front and back');
    const images = pdfRasters(pdf); assert.equal(images.length, 2, 'PDF embeds both card rasters');
    assertIconPixels(images[1], await evaluate('window.__exportGeometry'));
    assertContentPixels(images, await evaluate('window.__exportGeometry'));
    console.log(`PASS ${mode}: two-page PDF contains all four icons, Tamil Facebook name and office without a President address section`);
    if (mode === 'missing-addresses') {
      await command('Page.navigate', { url: `${origin}/register` });
      await until(() => evaluate("document.querySelector('.form-panel form') !== null"), 'registration form');
      assert.ok(await evaluate("!document.querySelector('input[autocomplete=one-time-code]') && [...document.querySelectorAll('button')].some(button => button.textContent.trim() === 'Continue')"), 'registration starts without OTP');
      await evaluate(`(() => {
        const fields = document.querySelectorAll('.form-panel input');
        const setter = Object.getOwnPropertyDescriptor(HTMLInputElement.prototype, 'value').set;
        for (const [field, value] of [[fields[0], 'Synthetic Member'], [fields[1], '9876543210']]) {
          setter.call(field, value); field.dispatchEvent(new Event('input', { bubbles: true }));
        }
        document.querySelector('.form-panel form').requestSubmit();
      })()`);
      await until(() => evaluate("document.querySelector('.registration-form') !== null"), 'registration details without OTP');
      assert.ok(await evaluate("!document.querySelector('input[autocomplete=one-time-code]')"));
      console.log('PASS registration: identity continues to details without OTP or a write request');
    }
    if (mode === 'approved-long-addresses') {
      await command('Page.navigate', { url: `${origin}/register` });
      await until(() => evaluate("document.querySelector('.form-panel form') !== null"), 'OTP-required registration form');
      assert.ok(await evaluate("[...document.querySelectorAll('button')].some(button => button.textContent.trim() === 'Send OTP') && !document.querySelector('.registration-form')"), 'explicit required-OTP mode retains verification step');
      console.log('PASS registration: required-OTP mode retains the verification step');
    }
    socket.close(); socket = null;
    chrome.kill(); await new Promise(resolve => chrome.once('exit', resolve)); chrome = null;
    frontend.kill(); await new Promise(resolve => frontend.once('exit', resolve)); frontend = null;
  }
  assert.equal(unexpectedRequests, 0, 'no registration/write or unexpected API requests');
  console.log(`ALL SOCIAL EXPORT CHECKS PASSED. No MongoDB access. Artifacts: ${runRoot}`);
} finally {
  socket?.close(); chrome?.kill(); frontend?.kill();
  await new Promise(resolve => mock.close(resolve));
}
