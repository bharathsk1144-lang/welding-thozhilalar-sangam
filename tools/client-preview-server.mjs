import { createServer } from 'node:http';
import { readFile, readdir } from 'node:fs/promises';
import path from 'node:path';
import { fileURLToPath, pathToFileURL } from 'node:url';
import { createHash } from 'node:crypto';

const root = fileURLToPath(new URL('../client/dist-client-preview/', import.meta.url));
const mime = { '.html': 'text/html; charset=utf-8', '.js': 'text/javascript; charset=utf-8', '.css': 'text/css; charset=utf-8', '.png': 'image/png', '.txt': 'text/plain; charset=utf-8' };
const forbidden = /localhost:5087|127\.0\.0\.1:5087|:27017|\/api\/|\/uploads\/|developmentOtp|verificationToken|wtsUserToken|adminToken|PasswordHash|AadhaarPhotoPath/;

export async function createPreviewServer() {
  // Load a narrow static inventory into memory. No arbitrary per-request disk access or proxy.
  const files = new Map();
  async function load(directory, prefix = '') {
    for (const entry of await readdir(directory, { withFileTypes: true })) {
      const relative = `${prefix}${entry.name}`;
      if (entry.isDirectory() && relative === 'assets') { await load(path.join(directory, entry.name), 'assets/'); continue; }
      if (!entry.isFile() || !/^(?:index\.html|robots\.txt|wts-logo-client-transparent\.png|assets\/[\w-]+\.(?:js|css))$/.test(relative)) {
        throw new Error('Unexpected file in client-preview output. Refusing to serve.');
      }
      const data = await readFile(path.join(directory, entry.name));
      if (/\.(?:html|js|css)$/.test(relative) && forbidden.test(data.toString('utf8'))) throw new Error('Live/private code detected in preview. Refusing to serve.');
      files.set(`/${relative}`, { data, type: mime[path.extname(relative)] });
    }
  }
  await load(root);
  if (!files.has('/index.html') || !files.has('/robots.txt')) throw new Error('Preview build is incomplete.');
  const logo = files.get('/wts-logo-client-transparent.png');
  if (!logo || createHash('sha256').update(logo.data).digest('hex') !== 'edd19e2d92f02bb473436267cf6845e51a3efe07012e2f66ec2c40d9802ec4f5') throw new Error('Unexpected preview logo artwork.');
  const routes = /^\/(?:about|contact|districts|register|user-login|forgot-password|my-id-card|verify-member|admin(?:\/[\w-]+)*|id-card(?:\/[\w-]+)*|payment(?:\/[\w-]+)*)?\/?$/;
  return createServer((request, response) => {
    response.setHeader('Content-Security-Policy', "default-src 'none'; script-src 'self'; style-src 'self'; img-src 'self' data:; font-src 'self'; connect-src 'none'; form-action 'none'; frame-ancestors 'none'; base-uri 'none'; object-src 'none'");
    response.setHeader('Cache-Control', 'no-store');
    response.setHeader('X-Robots-Tag', 'noindex, nofollow, noarchive');
    response.setHeader('X-Content-Type-Options', 'nosniff');
    response.setHeader('Referrer-Policy', 'no-referrer');
    if (!['GET', 'HEAD'].includes(request.method)) { response.writeHead(405, { Allow: 'GET, HEAD' }); response.end(); return; }
    let pathname;
    try { pathname = decodeURIComponent(new URL(request.url, 'http://preview.local').pathname); }
    catch { response.writeHead(400); response.end(); return; }
    if (pathname.includes('\\') || pathname.split('/').some(part => part.startsWith('.'))) { response.writeHead(404); response.end(); return; }
    const file = files.get(pathname) || (routes.test(pathname) ? files.get('/index.html') : null);
    if (!file) { response.writeHead(404); response.end(); return; }
    response.writeHead(200, { 'Content-Type': file.type, 'Content-Length': file.data.length });
    response.end(request.method === 'HEAD' ? undefined : file.data);
  });
}

if (process.argv[1] && import.meta.url === pathToFileURL(path.resolve(process.argv[1])).href) {
  const argument = process.argv[2] || '--port=4173';
  if (!/^--port=\d+$/.test(argument)) throw new Error('Use only --port=<preview-port>. The host/output directory are fixed.');
  const port = Number(argument.slice(7));
  if (port < 1024 || port > 65535 || [5087, 5173, 27017].includes(port)) throw new Error('Unsafe preview port.');
  const server = await createPreviewServer();
  server.listen(port, '127.0.0.1', () => console.log(`WTS static client preview: http://127.0.0.1:${port}`));
}
