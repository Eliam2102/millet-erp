import { type ReactNode } from 'react';
import { Button } from '@/components/ui/button';
import { cn } from '@/lib/utils';

/**
 * <c>&lt;InlineFormShell/&gt;</c> — wrapper visual de los inline forms
 * de edición de catálogos (border-amber). El contenido (campos) lo
 * inyecta el caller; este shell solo provee layout, footer con
 * botones y aria-label.
 */
export interface InlineFormShellProps {
  ariaLabel: string;
  onSubmit: (e: React.FormEvent) => void;
  onCancel: () => void;
  isPending: boolean;
  submitLabel?: string;
  pendingLabel?: string;
  children: ReactNode;
  className?: string;
}

export function InlineFormShell({
  ariaLabel,
  onSubmit,
  onCancel,
  isPending,
  submitLabel = 'Guardar',
  pendingLabel = 'Guardando…',
  children,
  className,
}: InlineFormShellProps) {
  return (
    <form
      onSubmit={onSubmit}
      noValidate
      className={cn(
        'space-y-3 rounded-md border border-amber-400 bg-amber-50/40 p-3',
        className,
      )}
      aria-label={ariaLabel}
      onKeyDown={(e) => {
        if (e.key === 'Escape') {
          e.preventDefault();
          onCancel();
        }
      }}
    >
      {children}
      <div className="flex flex-wrap items-center justify-end gap-2 border-t pt-2">
        <Button
          type="button"
          variant="ghost"
          size="sm"
          onClick={onCancel}
          disabled={isPending}
        >
          Cancelar
        </Button>
        <Button type="submit" size="sm" disabled={isPending}>
          {isPending ? pendingLabel : submitLabel}
        </Button>
      </div>
    </form>
  );
}

export interface FieldInlineProps {
  label: string;
  required?: boolean;
  hint?: string;
  error?: string;
  className?: string;
  children: ReactNode;
}

export function FieldInline({
  label,
  required,
  hint,
  error,
  className,
  children,
}: FieldInlineProps) {
  return (
    <div className={className}>
      <label className="mb-1 flex items-center gap-1 text-xs font-medium">
        {label}
        {required && (
          <span aria-hidden="true" className="text-rose-600">
            *
          </span>
        )}
      </label>
      {children}
      {hint != null && error == null && (
        <p className="mt-1 text-xs text-muted-foreground">{hint}</p>
      )}
      {error != null && (
        <p role="alert" className="mt-1 text-xs text-rose-600">
          {error}
        </p>
      )}
    </div>
  );
}
