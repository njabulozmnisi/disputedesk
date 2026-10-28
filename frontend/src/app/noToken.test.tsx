import { screen } from '@testing-library/react';

import { appSourceFiles } from '@/test/sourceFiles';
import { renderApp, signInAs } from '@/test/support';

// Criterion 3: the browser never sees a token.
describe('no token in the browser', () => {
  const forbidden = [
    /localStorage/,
    /sessionStorage/,
    /document\.cookie/,
    /indexedDB/,
    /access_token|id_token|refresh_token|id_token_hint/,
    /\bBearer\b|['"]Authorization['"]/i,
  ];

  it.each(forbidden.map((pattern) => [pattern.source, pattern] as const))(
    'no source file matches %s',
    (_name, pattern) => {
      const offenders = appSourceFiles(['.ts', '.tsx']).filter(({ text }) => pattern.test(text));

      expect(offenders.map(({ path }) => path)).toEqual([]);
    },
  );

  it('signs in by navigating to the BFF login, from every Sign in button', async () => {
    signInAs('anonymous');
    renderApp({ path: '/' });

    await screen.findByRole('heading', {
      level: 1,
      name: 'Something wrong with a card transaction?',
    });
    const links = [
      ...screen.getAllByRole('link', { name: 'Sign in' }),
      screen.getByRole('link', { name: 'Create a login' }),
    ];
    expect(links).toHaveLength(3);
    for (const link of links) {
      expect(link).toHaveAttribute('href', '/api/v1/bff/login');
    }
  });

  it('signs in again from the session-ended screen by navigating to the BFF login', async () => {
    signInAs('anonymous');
    renderApp({ path: '/signed-out?reason=expired' });

    expect(await screen.findByRole('link', { name: 'Sign in again' })).toHaveAttribute(
      'href',
      '/api/v1/bff/login',
    );
  });
});
