import {
  cloneElement,
  isValidElement,
  useId,
  type ReactElement,
  type ReactNode,
} from 'react';
import { cn } from '@/lib/utils';

/** Props de accesibilidad que `Field` entrega al control. */
export interface FieldControlProps {
  id: string;
  'aria-invalid'?: true;
  'aria-describedby'?: string;
  'aria-required'?: true;
}

export interface FieldProps {
  label: ReactNode;
  error?: string;
  required?: boolean;
  /** Muestra «(opcional)» junto al label. */
  optional?: boolean;
  hint?: ReactNode;
  className?: string;
  /**
   * Un elemento (se clona inyectando `id` y `aria-*`; si ya trae `id` se respeta)
   * o una función que recibe las props para aplicarlas donde corresponda
   * (p. ej. `inputProps` de `MoneyField`/`DecimalField`).
   */
  children: ReactElement | ((control: FieldControlProps) => ReactNode) | ReactNode;
}

// Elementos DOM que no son controles: no se les inyecta aria (un div no es "invalid").
const HOST_NO_CONTROL = /^(div|span|p|section|fieldset|ul|ol|li|label)$/;

/**
 * Campo de formulario accesible: label asociado por `htmlFor`, `*` visual
 * (la semántica la da `aria-required` en el control), hint y error enlazados
 * por `aria-describedby`. Sirve con `register` y con `Controller`.
 */
export function Field({
  label,
  error,
  required,
  optional,
  hint,
  className,
  children,
}: FieldProps) {
  const autoId = useId();
  const hintId = hint != null ? `${autoId}-hint` : undefined;
  const errorId = error ? `${autoId}-error` : undefined;
  const describedBy = [hintId, errorId].filter(Boolean).join(' ') || undefined;

  const childId =
    isValidElement<{ id?: string }>(children) ? children.props.id : undefined;
  const id = childId ?? autoId;

  const control: FieldControlProps = {
    id,
    ...(error ? { 'aria-invalid': true as const } : {}),
    ...(describedBy ? { 'aria-describedby': describedBy } : {}),
    ...(required ? { 'aria-required': true as const } : {}),
  };

  let body: ReactNode;
  if (typeof children === 'function') {
    body = (children as (c: FieldControlProps) => ReactNode)(control);
  } else if (
    isValidElement(children) &&
    !(typeof children.type === 'string' && HOST_NO_CONTROL.test(children.type))
  ) {
    body = cloneElement(children as ReactElement<Record<string, unknown>>, {
      ...control,
    });
  } else {
    body = children;
  }

  return (
    <div className={cn('flex flex-col gap-1.5', className)}>
      <label htmlFor={id} className="text-xs font-medium text-ink-strong">
        {label}
        {required && (
          <span aria-hidden="true" className="ml-0.5 text-danger-fg">
            *
          </span>
        )}
        {optional && (
          <span className="ml-1 font-normal text-ink-muted">(opcional)</span>
        )}
      </label>
      {body}
      {hint != null && (
        <p id={hintId} className="text-xs text-ink-muted">
          {hint}
        </p>
      )}
      {error && (
        <p id={errorId} className="text-xs text-danger-fg">
          {error}
        </p>
      )}
    </div>
  );
}
