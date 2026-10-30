import tailwindcss from '@tailwindcss/vite';
import react from '@vitejs/plugin-react';
import { resolve } from 'node:path';

import { defineConfig } from 'vitest/config';

// The browser only ever calls same-origin /api. In development and preview, Vite forwards it to the gateway of the
// local Compose stack, which holds the session cookie and the BFF.
const gateway = process.env.GATEWAY_ORIGIN ?? 'http://localhost:8080';

export default defineConfig({
  plugins: [react(), tailwindcss()],
  resolve: { tsconfigPaths: true },
  // /otlp is the browser's OTLP/HTTP trace export; it goes through the gateway too, which strips Cookie and
  // Authorization (story 2.5), never straight to the Aspire Dashboard.
  server: { proxy: { '/api': gateway, '/otlp': gateway } },
  preview: { proxy: { '/api': gateway, '/otlp': gateway } },
  test: {
    // The exporter package picks its browser build or its Node build by the `node` condition, and Vitest runs under
    // Node. Tests use the browser build, as the app does, so the export goes through `fetch`.
    alias: {
      '@opentelemetry/exporter-trace-otlp-http': resolve(
        import.meta.dirname,
        'node_modules/@opentelemetry/exporter-trace-otlp-http/dist/platform/browser/index.mjs',
      ),
    },
    globals: true,
    restoreMocks: true,
    environment: 'jsdom',
    setupFiles: ['./src/test/setup.ts'],
    include: ['src/**/*.test.{ts,tsx}'],
  },
});
