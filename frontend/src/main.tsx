import './zodConfig';

import { QueryClientProvider } from '@tanstack/react-query';
import { RouterProvider } from '@tanstack/react-router';
import { StrictMode } from 'react';
import { createRoot } from 'react-dom/client';

import { createQueryClient } from '@/api/queryClient';
import { createAppRouter } from '@/app/router';
import { startTracing } from '@/telemetry/startTracing';

import './index.css';

// The gateway in AWS takes in no browser telemetry (AD-29), so that build sets VITE_BROWSER_TELEMETRY=off. Only the
// exact value `off` turns tracing off; Vite replaces the variable at build time.
if (import.meta.env.VITE_BROWSER_TELEMETRY !== 'off') startTracing();

// The query client's error handlers read the router only when they run, so the client is created first.
const queryClient = createQueryClient(() => router);
const router = createAppRouter(queryClient);
const container = document.getElementById('root');
if (!container) throw new Error('index.html has no #root element');

createRoot(container).render(
  <StrictMode>
    <QueryClientProvider client={queryClient}>
      <RouterProvider router={router} />
    </QueryClientProvider>
  </StrictMode>,
);
