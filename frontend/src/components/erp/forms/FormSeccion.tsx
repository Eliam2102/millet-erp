import { useId, type ComponentPropsWithoutRef, type ReactNode } from 'react';
import { cn } from '@/lib/utils';
import { FormGrid } from './FormGrid';

export interface FormSeccionProps extends ComponentPropsWithoutRef<'fieldset'> {
  numero?: number;
  titulo: string;
  descripcion?: ReactNode;
}
/** Agrupa campos con leyenda accesible y la misma rejilla de doce columnas. */
export function FormSeccion({
  numero,
  titulo,
  descripcion,
  children,
  className,
  'aria-describedby': describedBy,
  ...props
}: FormSeccionProps) {
  const id = useId();
  return (
    <fieldset
      className={cn('min-w-0 space-y-3.5', className)}
      aria-describedby={
        [describedBy, descripcion != null ? id : undefined].filter(Boolean).join(' ') || undefined
      }
      {...props}
    >
      <legend className="text-sm font-semibold text-ink">
        {numero != null && `${numero}. `}
        {titulo}
      </legend>
      {descripcion != null && (
        <p id={id} className="text-xs text-ink-muted">
          {descripcion}
        </p>
      )}
      <FormGrid>{children}</FormGrid>
    </fieldset>
  );
}
