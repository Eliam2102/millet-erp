import { Lock } from 'lucide-react';
import { cn } from '@/lib/utils';

/** Callout de advertencia (DESIGN 4.10): role="note", tokens warning-note, ícono de candado por tratarse de campos bloqueados. */
export function AvisoNota({ children, className, ...props }: React.ComponentProps<'p'>) {
  return (
    <p
      role="note"
      className={cn('flex items-start gap-2 rounded-md bg-warning-note-bg px-3 py-2.5 text-sm text-warning-note-fg', className)}
      {...props}
    >
      <Lock className="mt-0.5 size-4 shrink-0" strokeWidth={1.6} aria-hidden="true" />
      <span>{children}</span>
    </p>
  );
}
