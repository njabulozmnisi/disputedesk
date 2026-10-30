import { http, HttpResponse } from 'msw';
import { setupServer } from 'msw/node';

// The tracing set-up exports to the page's own origin; a test that reads the export adds its own handler first.
export const server = setupServer(http.post('*/otlp/v1/traces', () => HttpResponse.json({})));
