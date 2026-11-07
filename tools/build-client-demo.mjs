// Explicit opt-in applies to this process/build only. Normal production never imports preview/main.jsx.
import { build } from '../client/node_modules/vite/dist/node/index.js';
import { fileURLToPath } from 'node:url';
process.env.WTS_CLIENT_PREVIEW_DEMO = 'true';
await build({ configFile: fileURLToPath(new URL('../client/vite.client-preview.config.js', import.meta.url)), mode: 'client-preview' });
