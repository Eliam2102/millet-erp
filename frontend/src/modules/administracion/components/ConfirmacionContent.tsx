import type { ComponentProps } from 'react';
import { AlertDialogContent } from '@/components/ui/alert-dialog';
import { cn } from '@/lib/utils';

/** Conserva la semántica y el foco de la confirmación Radix con la receta DS. */
export function ConfirmacionContent({
  className,
  size = 'sm',
  ...props
}: ComponentProps<typeof AlertDialogContent> & { size?: 'sm' }) {
  return (
    <AlertDialogContent
      data-size={size}
      className={cn(
        'flex max-h-[90vh] w-full max-w-120 flex-col gap-0 overflow-hidden rounded-2xl border-line bg-surface-card p-0 text-ink shadow-dialog',
        className,
      )}
      {...props}
    />
  );
}
