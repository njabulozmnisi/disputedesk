import * as React from 'react';
import { AlertDialog as AlertDialogPrimitive } from 'radix-ui';

import { Button } from '@/components/ui/button';
import { cn } from '@/lib/utils';

// shortcut: Radix scroll lock (react-remove-scroll, through react-style-singleton) injects a <style> with CSS text that the
// gateway CSP (style-src 'self' and the empty-string hash) blocks. Each time a dialog opens the browser logs one CSP
// violation in the console, and the page behind the dialog can still scroll; the dialog itself works, and focus is trapped by
// Radix, not by that style. Accepted: the CSP stays strict. Upgrade if the dialog must lock scrolling or the console must
// stay clean: the gateway answers with a per-response nonce in style-src and in the page, and the SPA passes it to
// react-style-singleton with get-nonce's setNonce (a gateway change, then a one-line call in main.tsx).
function AlertDialog({ ...props }: React.ComponentProps<typeof AlertDialogPrimitive.Root>) {
  return <AlertDialogPrimitive.Root data-slot="alert-dialog" {...props} />;
}

function AlertDialogOverlay({
  className,
  ...props
}: React.ComponentProps<typeof AlertDialogPrimitive.Overlay>) {
  return (
    <AlertDialogPrimitive.Overlay
      data-slot="alert-dialog-overlay"
      className={cn(
        'fixed inset-0 z-50 bg-overlay data-[state=closed]:animate-out data-[state=closed]:fade-out-0 data-[state=open]:animate-in data-[state=open]:fade-in-0',
        className,
      )}
      {...props}
    />
  );
}

function AlertDialogContent({
  className,
  ...props
}: React.ComponentProps<typeof AlertDialogPrimitive.Content>) {
  return (
    <AlertDialogPrimitive.Portal>
      <AlertDialogOverlay />
      <AlertDialogPrimitive.Content
        data-slot="alert-dialog-content"
        className={cn(
          // A bottom sheet below 768px, a centred dialog from 768px. Either way the content is capped at the viewport height and
          // scrolls inside, so the title and both buttons stay reachable at 320px and at 200% zoom (WCAG 2.2 SC 1.4.10).
          'fixed inset-x-0 bottom-0 z-50 grid max-h-dvh w-full gap-4 overflow-y-auto rounded-t-lg bg-card px-4 py-6 text-card-foreground shadow-overlay md:inset-x-auto md:top-1/2 md:bottom-auto md:left-1/2 md:max-w-(--form-max) md:-translate-x-1/2 md:-translate-y-1/2 md:rounded-lg md:p-8',
          className,
        )}
        {...props}
      />
    </AlertDialogPrimitive.Portal>
  );
}

function AlertDialogHeader({ className, ...props }: React.ComponentProps<'div'>) {
  return (
    <div
      data-slot="alert-dialog-header"
      className={cn('grid gap-2 text-left', className)}
      {...props}
    />
  );
}

function AlertDialogFooter({ className, ...props }: React.ComponentProps<'div'>) {
  return (
    <div
      data-slot="alert-dialog-footer"
      className={cn('flex flex-col gap-3 border-t border-border pt-4 md:flex-row', className)}
      {...props}
    />
  );
}

function AlertDialogTitle({
  className,
  ...props
}: React.ComponentProps<typeof AlertDialogPrimitive.Title>) {
  return (
    <AlertDialogPrimitive.Title
      data-slot="alert-dialog-title"
      className={cn('text-h2 font-strong text-heading', className)}
      {...props}
    />
  );
}

function AlertDialogDescription({
  className,
  ...props
}: React.ComponentProps<typeof AlertDialogPrimitive.Description>) {
  return (
    <AlertDialogPrimitive.Description
      data-slot="alert-dialog-description"
      className={cn('text-muted-foreground', className)}
      {...props}
    />
  );
}

function AlertDialogAction({
  className,
  variant,
  ...props
}: React.ComponentProps<typeof AlertDialogPrimitive.Action> &
  Pick<React.ComponentProps<typeof Button>, 'variant'>) {
  return (
    <Button variant={variant} asChild>
      <AlertDialogPrimitive.Action
        data-slot="alert-dialog-action"
        className={cn(className)}
        {...props}
      />
    </Button>
  );
}

// Radix focuses the Cancel button when the dialog opens: the least destructive action (WAI-ARIA APG, alert dialog).
function AlertDialogCancel({
  className,
  variant = 'secondary',
  ...props
}: React.ComponentProps<typeof AlertDialogPrimitive.Cancel> &
  Pick<React.ComponentProps<typeof Button>, 'variant'>) {
  return (
    <Button variant={variant} asChild>
      <AlertDialogPrimitive.Cancel
        data-slot="alert-dialog-cancel"
        className={cn(className)}
        {...props}
      />
    </Button>
  );
}

export {
  AlertDialog,
  AlertDialogAction,
  AlertDialogCancel,
  AlertDialogContent,
  AlertDialogDescription,
  AlertDialogFooter,
  AlertDialogHeader,
  AlertDialogTitle,
};
