import tailwindcss from '@tailwindcss/vite';
import react from '@vitejs/plugin-react';
import { defineConfig } from 'vitest/config';

// The browser only ever calls same-origin /api. In development and preview, Vite forwards it to the gateway of the
// local Compose stack, which holds the session cookie and the BFF.
const gateway = process.env.GATEWAY_ORIGIN ?? 'http://localhost:8080';

export default defineConfig({
  plugins: [react(), tailwindcss()],
  resolve: { tsconfigPaths: true },
  server: { proxy: { '/api': gateway } },
  preview: { proxy: { '/api': gateway } },
  test: {
    globals: true,
    restoreMocks: true,
    environment: 'jsdom',
    setupFiles: ['./src/test/setup.ts'],
    include: ['src/**/*.test.{ts,tsx}'],
  },
});
