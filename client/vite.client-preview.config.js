import { defineConfig } from 'vite';
import react from '@vitejs/plugin-react';
import { readFileSync } from 'node:fs';
import { fileURLToPath } from 'node:url';

const local = path => fileURLToPath(new URL(path, import.meta.url));

export default defineConfig({
  root: local('./preview/'),
  envDir: local('./preview/'),
  // No normal VITE_ process/file values enter this public design build.
  envPrefix: '__WTS_NO_CLIENT_PREVIEW_ENV__',
  publicDir: false,
  define: { __WTS_PREVIEW_DEMO__: JSON.stringify(process.env.WTS_CLIENT_PREVIEW_DEMO === 'true') },
  resolve: { alias: [
    { find: /^\.\.\/components\/MembershipStatistics\.jsx$/, replacement: local('./preview/PreviewStatistics.jsx') },
    { find: /^\.\/ProtectedImage\.jsx$/, replacement: local('./preview/DemoPassport.jsx') }
  ] },
  plugins: [react(), {
    name: 'wts-public-preview-isolation',
    configResolved(config) {
      const allowed = new Set(['BASE_URL', 'MODE', 'DEV', 'PROD']);
      if (Object.keys(config.env).some(key => !allowed.has(key))) throw new Error('Unexpected preview environment value. Refusing to build.');
    },
    generateBundle() {
      for (const id of this.getModuleIds()) {
        if (id.replaceAll('\\', '/').endsWith('/src/api/memberId.js')) continue; // Pure display/filename helpers, no I/O.
        if (/\/src\/api\/|\/src\/pages\/(?:Admin|Registration|UserLogin|ForgotPassword|IdCard|MyIdCard|UserDetails|VerifyMember)|\/server\//i.test(id.replaceAll('\\', '/'))) {
          throw new Error('A live/private module entered the public preview. Refusing to build.');
        }
      }
      this.emitFile({ type: 'asset', fileName: 'wts-logo-client-transparent.png', source: readFileSync(local('./public/wts-logo-client-transparent.png')) });
      this.emitFile({ type: 'asset', fileName: 'robots.txt', source: 'User-agent: *\nDisallow: /\n' });
    }
  }],
  build: { outDir: local('./dist-client-preview/'), emptyOutDir: true, sourcemap: false }
});
