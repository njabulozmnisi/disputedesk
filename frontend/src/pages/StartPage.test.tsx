import { screen } from '@testing-library/react';

import { expectDecorativeIcon } from '@/test/icons';
import { renderApp, signInAs } from '@/test/support';

describe('the start page', () => {
  it('draws the sample badge with a decorative Lucide search icon', async () => {
    signInAs('anonymous');
    renderApp({ path: '/' });
    await screen.findByRole('heading', {
      level: 1,
      name: 'Something wrong with a card transaction?',
    });

    const sample = screen.getByText('A sample dispute.', { exact: false }).closest('figure');
    expectDecorativeIcon(sample?.querySelector('svg.lucide-search'));
  });
});
