import { act, fireEvent, render, screen } from '@testing-library/react';
import { vi } from 'vitest';

import { ProblemNotice } from './ProblemNotice';

const reference = '4bf92f3577b34da6a3ce929d0e0e4736';
const referenceSentence = 'If this keeps happening, contact the bank and quote this reference.';

// Criterion 2: the reference sits on its own row, after the sentence and the next step. The 44px height, the 20%
// hairline colour and the layout are QA's visual checks: jsdom has no layout.
describe('ProblemNotice', () => {
  it('is a danger alert with the sentence, the next step and the reference sentence', () => {
    render(<ProblemNotice message="It broke." nextStep="Try again." traceId={reference} />);

    const alert = screen.getByRole('alert');
    expect(alert.querySelector('svg.lucide-triangle-alert')).toBeInTheDocument();
    expect(alert).toHaveTextContent('It broke.');
    expect(alert).toHaveTextContent('Try again.');
    expect(screen.getByText(referenceSentence)).toBeInTheDocument();
  });

  it('puts the label, the trace ID in mono and the Copy button on one row after a hairline', () => {
    render(<ProblemNotice message="It broke." traceId={reference} />);

    const traceId = screen.getByText(reference);
    expect(traceId).toHaveClass('font-mono');
    const row = traceId.parentElement;
    expect(row).toHaveTextContent('Reference');
    expect(row).toContainElement(screen.getByRole('button', { name: 'Copy' }));
    expect(row).toHaveClass('border-t');
    // The notice's description grid centres its items at the start; the row stretches so the hairline crosses the notice.
    expect(row).toHaveClass('justify-self-stretch');
    expect(row?.previousElementSibling).toHaveTextContent(referenceSentence);
  });

  it('makes the Copy button at least the 44px tap target', () => {
    render(<ProblemNotice message="It broke." traceId={reference} />);

    expect(screen.getByRole('button', { name: 'Copy' })).toHaveClass('min-h-(--tap-target)');
  });

  it('builds the Copy pill from the shared Button, so it carries the design system pill and outline', () => {
    render(<ProblemNotice message="It broke." traceId={reference} />);

    const copy = screen.getByRole('button', { name: 'Copy' });
    expect(copy).toHaveAttribute('data-slot', 'button');
    expect(copy).toHaveClass('rounded-full');
  });

  it('has no reference sentence, hairline or row without a trace ID', () => {
    const { container } = render(<ProblemNotice message="It broke." nextStep="Try again." />);

    expect(screen.queryByText(referenceSentence)).not.toBeInTheDocument();
    expect(screen.queryByText('Reference')).not.toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'Copy' })).not.toBeInTheDocument();
    expect(container.querySelector('.border-t')).not.toBeInTheDocument();
  });

  it('renders an action inside the alert after the reference row', () => {
    render(
      <ProblemNotice
        message="It broke."
        traceId={reference}
        action={<button type="button">Sign out</button>}
      />,
    );

    const alert = screen.getByRole('alert');
    const action = screen.getByRole('button', { name: 'Sign out' });
    expect(alert).toContainElement(action);
    const copy = screen.getByRole('button', { name: 'Copy' });
    expect(copy.compareDocumentPosition(action) & Node.DOCUMENT_POSITION_FOLLOWING).toBeTruthy();
  });

  it('renders an action after the next step when there is no trace ID', () => {
    render(
      <ProblemNotice
        message="It broke."
        nextStep="Try again."
        action={<button type="button">Sign out</button>}
      />,
    );

    const next = screen.getByText('Try again.');
    const action = screen.getByRole('button', { name: 'Sign out' });
    expect(screen.getByRole('alert')).toContainElement(action);
    expect(next.compareDocumentPosition(action) & Node.DOCUMENT_POSITION_FOLLOWING).toBeTruthy();
  });

  describe('Copy', () => {
    beforeEach(() => {
      vi.useFakeTimers({ toFake: ['setTimeout', 'clearTimeout'] });
    });
    afterEach(() => {
      vi.useRealTimers();
      vi.unstubAllGlobals();
    });

    async function clickCopy(writeText: () => Promise<void>) {
      vi.stubGlobal('navigator', { clipboard: { writeText } });
      render(<ProblemNotice message="It broke." traceId={reference} />);
      fireEvent.click(screen.getByRole('button', { name: 'Copy' }));
      // Lets the write's promise settle; `findBy` would poll with the faked timers.
      await act(async () => {
        await Promise.resolve();
      });
    }

    it('writes the trace ID and says Copied for 2 seconds', async () => {
      const writeText = vi.fn().mockResolvedValue(undefined);

      await clickCopy(writeText);

      expect(writeText).toHaveBeenCalledWith(reference);
      expect(screen.getByRole('button', { name: 'Copied' })).toBeInTheDocument();
      act(() => {
        vi.advanceTimersByTime(1999);
      });
      expect(screen.getByRole('button', { name: 'Copied' })).toBeInTheDocument();
      act(() => {
        vi.advanceTimersByTime(1);
      });
      expect(screen.getByRole('button', { name: 'Copy' })).toBeInTheDocument();
    });

    it('keeps saying Copy when the write fails', async () => {
      await clickCopy(vi.fn().mockRejectedValue(new Error('denied')));

      expect(screen.getByRole('button', { name: 'Copy' })).toBeInTheDocument();
      expect(screen.queryByRole('button', { name: 'Copied' })).not.toBeInTheDocument();
    });
  });
});
