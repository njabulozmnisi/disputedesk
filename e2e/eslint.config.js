// Only the two wait rules: Playwright's reference calls a fixed-time wait inherently flaky. The plugin rule checks a
// receiver named `page` only, so core no-restricted-properties covers every other receiver (`second.waitForTimeout`).
import playwright from 'eslint-plugin-playwright';
import tseslint from 'typescript-eslint';

export default [
  {
    files: ['tests/**/*.ts'],
    languageOptions: { parser: tseslint.parser },
    plugins: { playwright },
    rules: {
      'playwright/no-wait-for-timeout': 'error',
      'no-restricted-properties': ['error', { property: 'waitForTimeout', message: 'Wait for a response, focus or event, not for time.' }],
    },
  },
];
