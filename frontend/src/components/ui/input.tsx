import { type ComponentProps } from 'react';
import { cn } from '@/lib/utils';

export function Input({
  className,
  type,
  ...props
}: ComponentProps<'input'>) {
  return (
    <input
      data-slot="input"
      type={type}
      className={cn(
        'flex h-ctl-xl max-sm:min-h-touch w-full rounded-md border border-line-control bg-surface-card px-3 py-1 text-sm text-ink transition-colors file:border-0 file:bg-transparent file:text-sm file:font-medium placeholder:text-ink-muted focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-brand aria-invalid:border-danger disabled:cursor-not-allowed disabled:opacity-50',
        className,
      )}
      {...props}
    />
  );
}
