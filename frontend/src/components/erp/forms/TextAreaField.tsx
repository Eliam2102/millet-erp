import { useEffect, useRef, type ComponentProps } from 'react';
import { Textarea } from '@/components/ui/textarea';
import { cn } from '@/lib/utils';

/**
 * <c>&lt;TextAreaField/&gt;</c> — textarea autosize con counter de
 * caracteres opcional. Doc 05 §11.3.
 *
 * <para>Características:</para>
 * <list>
 *   <item>Autosize: ajusta <c>rows</c> según el contenido (entre
 *   <c>minRows</c> y <c>maxRows</c>).</item>
 *   <item><c>maxLength</c> opcional con counter visible debajo
 *   ("123 / 500") cuando se está cerca del límite.</item>
 *   <item>Empty input → <c>null</c> en el output (no <c>''</c>),
 *   coherente con el shape nullable de los DTOs del backend.</item>
 * </list>
 */
export interface TextAreaFieldProps {
  value: string | null | undefined;
  onChange: (value: string | null) => void;
  /** Filas mínimas de altura (autosize no baja de aquí). Default 3. */
  minRows?: number;
  /** Filas máximas. Default 10. */
  maxRows?: number;
  /** Tope de caracteres. Si se pasa, muestra counter cuando faltan ≤20%. */
  maxLength?: number;
  disabled?: boolean;
  className?: string;
  textareaProps?: Omit<
    ComponentProps<typeof Textarea>,
    'value' | 'onChange' | 'rows' | 'maxLength' | 'disabled'
  >;
}

export function TextAreaField({
  value,
  onChange,
  minRows = 3,
  maxRows = 10,
  maxLength,
  disabled,
  className,
  textareaProps,
}: TextAreaFieldProps) {
  const ref = useRef<HTMLTextAreaElement>(null);

  // Autosize: recalcula la altura cuando cambia el value.
  useEffect(() => {
    const el = ref.current;
    if (el == null) return;
    el.style.height = 'auto';
    const lineHeight = parseInt(getComputedStyle(el).lineHeight, 10) || 20;
    const minHeight = lineHeight * minRows;
    const maxHeight = lineHeight * maxRows;
    el.style.height = `${Math.min(Math.max(el.scrollHeight, minHeight), maxHeight)}px`;
    // Si scrollHeight > maxHeight, dejamos que el navegador renderice
    // el scrollbar interno del textarea.
    el.style.overflowY = el.scrollHeight > maxHeight ? 'auto' : 'hidden';
  }, [value, minRows, maxRows]);

  function handleChange(input: string) {
    if (input === '') {
      onChange(null);
      return;
    }
    onChange(input);
  }

  const length = value?.length ?? 0;
  const showCounter = maxLength != null && length >= maxLength * 0.8;

  return (
    <div className={cn('space-y-1', className)}>
      <Textarea
        ref={ref}
        value={value ?? ''}
        onChange={(e) => handleChange(e.target.value)}
        rows={minRows}
        maxLength={maxLength}
        disabled={disabled}
        {...textareaProps}
      />
      {showCounter && (
        <p
          className={cn(
            'text-right text-xs',
            length === maxLength
              ? 'text-rose-600'
              : 'text-muted-foreground',
          )}
          aria-live="polite"
        >
          {length} / {maxLength}
        </p>
      )}
    </div>
  );
}
