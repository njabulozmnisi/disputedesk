import '@testing-library/jest-dom/vitest';

import { cleanup } from '@testing-library/react';
import { afterAll, afterEach, beforeAll, expect } from 'vitest';
import * as axeMatchers from 'vitest-axe/matchers';

import { server } from './server';

// toHaveNoViolations: fails a test on any axe-core rule violation (a role without a name, a bad label, ...).
expect.extend(axeMatchers);

// jsdom has no layout, so it does not implement scrolling; the router calls it on navigation.
window.scrollTo = () => undefined;

// Every request a test makes must have a handler: an unexpected call to the API fails the test.
beforeAll(() => {
  server.listen({ onUnhandledFrame: 'error' });
});

afterEach(() => {
  cleanup();
  server.resetHandlers();
  server.events.removeAllListeners();
});

afterAll(() => {
  server.close();
});
