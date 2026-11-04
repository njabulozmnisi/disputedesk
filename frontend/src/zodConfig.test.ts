import { z } from 'zod';

import { appSourceFiles } from '@/test/sourceFiles';

// Criterion 3: zod parses without `new Function` (the CSP has no 'unsafe-eval'), set once, before any schema is built.
describe('the zod configuration', () => {
  it('is jitless in the test run, whose setup file imports it first', () => {
    expect(z.config().jitless).toBe(true);
  });

  it('is imported first in main.tsx, before any schema is constructed', () => {
    const main = appSourceFiles(['.tsx']).find(({ path }) => path === 'main.tsx');

    expect(main?.text.match(/^import .*$/m)?.[0]).toBe("import './zodConfig';");
  });

  it('is called in no other module', () => {
    const callers = appSourceFiles(['.ts', '.tsx'])
      .filter(({ text }) => text.includes('z.config('))
      .map(({ path }) => path);

    expect(callers).toEqual(['zodConfig.ts']);
  });
});
