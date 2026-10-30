import 'vitest';
import type { AxeMatchers } from 'vitest-axe/matchers';

// Types for the matcher, the way Vitest documents it (https://vitest.dev/guide/extending-matchers). The package's own
// `vitest-axe/extend-expect` types extend the legacy global `Vi` namespace, which Vitest 5 no longer reads. This is the
// same declaration @testing-library/jest-dom ships for its matchers.
declare module 'vitest' {
  interface Assertion {
    toHaveNoViolations: AxeMatchers['toHaveNoViolations'];
  }
}
