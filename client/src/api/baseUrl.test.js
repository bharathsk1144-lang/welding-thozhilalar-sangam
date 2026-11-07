import test from 'node:test';
import assert from 'node:assert/strict';
import { resolveApiBase } from './baseUrl.js';

test('desktop and LAN API addresses follow the browser host', () => {
  for (const hostname of ['localhost', '127.0.0.1', '192.168.1.20', '10.0.0.4']) {
    assert.equal(resolveApiBase('', { hostname, protocol: 'http:' }), `http://${hostname}:5087/api`);
  }
  assert.equal(resolveApiBase('', { hostname: '::1', protocol: 'http:' }), 'http://[::1]:5087/api');
  assert.equal(resolveApiBase('', { hostname: '[::1]', protocol: 'http:' }), 'http://[::1]:5087/api');
});
test('configured API endpoint and HTTPS are preserved', () => {
  assert.equal(resolveApiBase('https://api.example.test/api/', { hostname: 'localhost' }), 'https://api.example.test/api');
  assert.equal(resolveApiBase('', { hostname: '192.168.1.20', protocol: 'https:' }), 'https://192.168.1.20:5087/api');
});
