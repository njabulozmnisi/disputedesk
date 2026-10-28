import { defineConfig } from 'orval';

// Generates the TanStack Query hooks and the TypeScript models from the committed OpenAPI document.
// Nobody edits src/api/generated; run `npm run generate` after backend/openapi.json changes.
export default defineConfig({
  disputedesk: {
    input: '../backend/openapi.json',
    output: {
      target: 'src/api/generated/disputedesk.ts',
      schemas: 'src/api/generated/model',
      client: 'react-query',
      httpClient: 'fetch',
      mock: false,
      clean: true,
      override: {
        mutator: { path: 'src/api/mutator.ts', name: 'customFetch' },
        fetch: { includeHttpResponseReturnType: false },
        query: { useInfinite: true, useInfiniteQueryParam: 'cursor' },
      },
    },
  },
});
