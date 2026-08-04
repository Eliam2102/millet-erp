import { type ComponentProps } from 'react';
import { Input } from '@/components/ui/input';
import { cn } from '@/lib/utils';

/**
 * <c>&lt;DecimalField/&gt;</c> — input numérico para cantidades con
 * decimales. Doc 05 §11.3 + §10.4 del 01-diseno (5 decimales máx).
 *
 * <para>Diferencias con un <c>&lt;Input type="number"/&gt;</c> raw:</para>
 * <list>
 *   <item>API <c>value</c> + <c>onChange</c> tipados como
 *   <c>number | null</c> (no string).</item>
 *   <item><c>inputMode="decimal"</c> activa el teclado numérico con
 *   coma/punto en mobile.</item>
 *   <item><c>step</c> configurable (default <c>0.00001</c> = 5
 *   decimales).</item>
 *   <item>Empty input → <c>null</c> (no <c>NaN</c>); el caller
 *   distingue "vacío" de "0".</item>
 *   <item><c>tabular-nums</c> para alinear en tablas.</item>
 * </list>
 */
export interface DecimalFieldProps {
  value: number | null | undefined;
  onChange: (value: number | null) => void;
  /** Mínimo permitido (no enforced visualmente; Zod debería validar). */
  min?: number;
  max?: number;
  /** Step del input. Default <c>0.00001</c> (5 decimales). */
  step?: number;
  disabled?: boolean;
  className?: string;
  inputProps?: Omit<
    ComponentProps<typeof Input>,
    'value' | 'onChange' | 'type' | 'inputMode' | 'min' | 'max' | 'step'
  >;
}

export function DecimalField({
  value,
  onChange,
  min,
  max,
  step = 0.00001,
  disabled,
  className,
  inputProps,
}: DecimalFieldProps) {
  function handleChange(input: string) {
    if (input === '' || input === '-') {
      onChange(null);
      return;
    }
    const parsed = Number.parseFloat(input);
    if (!Number.isFinite(parsed)) return;
    onChange(parsed);
  }

  return (
    <Input
      type="number"
      inputMode="decimal"
      step={step}
      min={min}
      max={max}
      value={value ?? ''}
      onChange={(e) => handleChange(e.target.value)}
      disabled={disabled}
      className={cn('tabular-nums', className)}
      {...inputProps}
    />
  );
}
