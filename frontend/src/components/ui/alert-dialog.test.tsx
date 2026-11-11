import { render, screen } from '@testing-library/react';

import {
  AlertDialog,
  AlertDialogCancel,
  AlertDialogContent,
  AlertDialogDescription,
  AlertDialogFooter,
  AlertDialogHeader,
  AlertDialogTitle,
} from './alert-dialog';

// The content is a bottom sheet below 768px and a centred dialog from 768px, so its buttons stack in the sheet and sit
// side by side only in the dialog: the footer switches at the same md breakpoint as the content.
describe('AlertDialogFooter', () => {
  it('turns into a row at md, the breakpoint where the sheet becomes a dialog', () => {
    render(
      <AlertDialog open>
        <AlertDialogContent>
          <AlertDialogHeader>
            <AlertDialogTitle>Title</AlertDialogTitle>
            <AlertDialogDescription>Description</AlertDialogDescription>
          </AlertDialogHeader>
          <AlertDialogFooter>
            <AlertDialogCancel>Keep</AlertDialogCancel>
          </AlertDialogFooter>
        </AlertDialogContent>
      </AlertDialog>,
    );

    const footer = screen.getByRole('button', { name: 'Keep' }).parentElement;
    expect(footer).toHaveAttribute('data-slot', 'alert-dialog-footer');
    expect(footer).toHaveClass('flex-col', 'md:flex-row');
    expect(footer?.className).not.toMatch(/(^| )sm:flex-row/);
    expect(screen.getByRole('alertdialog')).toHaveClass('md:max-w-(--form-max)');
  });
});
