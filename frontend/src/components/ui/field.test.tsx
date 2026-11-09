import { render, screen } from '@testing-library/react';

import { expectDecorativeIcon } from '@/test/icons';

import { FieldError } from './field';

// Criterion 3: a field error leads with a 14px danger icon; its id and message are unchanged.
describe('FieldError', () => {
  it('leads with a 14px TriangleAlert before the message', () => {
    render(<FieldError id="code-error">Enter the 6-digit code.</FieldError>);

    const error = screen.getByText('Enter the 6-digit code.');
    expectDecorativeIcon(error.querySelector('svg.lucide-triangle-alert'), 'size-3.5');
    expect(error.firstElementChild).toHaveClass('lucide-triangle-alert');
  });

  it('keeps its id and passes its role through', () => {
    render(
      <FieldError id="code-error" role="alert">
        Wrong code.
      </FieldError>,
    );

    const error = screen.getByRole('alert');
    expect(error).toHaveAttribute('id', 'code-error');
    expect(error).toHaveTextContent('Wrong code.');
  });
});
