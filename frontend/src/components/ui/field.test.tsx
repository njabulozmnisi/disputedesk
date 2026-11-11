import { render, screen } from '@testing-library/react';

import { expectDecorativeIcon } from '@/test/icons';

import { Field, FieldError, FieldLabel } from './field';
import { Input } from './input';

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

// HANDOFF 3.9: a disabled field is drawn muted, its label included. jsdom has no Tailwind, so this checks the class
// that dims the label while the field holds a disabled control (CSS :has(), as the mockup's field:has(:disabled) label).
describe('Field', () => {
  it('dims its label while it holds a disabled control', () => {
    render(
      <Field>
        <FieldLabel htmlFor="id-number">ID number</FieldLabel>
        <Input id="id-number" disabled />
      </Field>,
    );

    expect(screen.getByRole('group')).toHaveClass('has-disabled:[&_label]:text-muted-foreground');
    expect(screen.getByLabelText('ID number')).toBeDisabled();
  });
});
