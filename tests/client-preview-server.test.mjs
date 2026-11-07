import test from 'node:test';
import assert from 'node:assert/strict';
import { once } from 'node:events';
import { createPreviewServer } from '../tools/client-preview-server.mjs';

test('static preview serves only approved build files and harmless SPA routes on loopback', async () => {
  const server = await createPreviewServer();
  server.listen(0, '127.0.0.1'); await once(server, 'listening');
  const base = `http://127.0.0.1:${server.address().port}`;
  try {
    assert.equal(server.address().address, '127.0.0.1');
    for (const route of ['/', '/about', '/contact', '/register', '/user-login', '/forgot-password', '/verify-member', '/my-id-card', '/id-card/placeholder', '/admin/login', '/admin/dashboard', '/admin/users/placeholder']) {
      const response = await fetch(base + route);
      assert.equal(response.status, 200, route);
      assert.match(await response.text(), /Client Preview/);
      assert.match(response.headers.get('content-security-policy'), /connect-src 'none'/);
      assert.match(response.headers.get('content-security-policy'), /form-action 'none'/);
      assert.match(response.headers.get('x-robots-tag'), /noindex/);
      assert.equal(response.headers.get('cache-control'), 'no-store');
    }
    const head = await fetch(base, { method: 'HEAD' }); assert.equal(head.status, 200); assert.equal(await head.text(), '');
    const logo = await fetch(base + '/wts-logo-client-transparent.png'); assert.equal(logo.headers.get('content-type'), 'image/png');
    for (const route of ['/api/statistics', '/api/users', '/uploads/member.png', '/.env', '/server/appsettings.json', '/server/appsettings.Statistics.local.json', '/src/main.jsx', '/@fs/C:/Windows/win.ini', '/node_modules/react/index.js', '/assets/../.env', '/%2eenv', '/artifacts/backup.json', '/payment-qr.png']) {
      assert.equal((await fetch(base + route)).status, 404, route);
    }
    for (const method of ['POST', 'PUT', 'PATCH', 'DELETE', 'OPTIONS']) {
      assert.equal((await fetch(base + '/register', { method })).status, 405, method);
    }
  } finally { server.closeAllConnections(); await new Promise(resolve => server.close(resolve)); }
});
