import { useState } from 'react';
import { CalendarIcon } from 'lucide-react';
import { format, parseISO, isValid } from 'date-fns';
import { es } from 'date-fns/locale';
import { Button } from '@/components/ui/button';
import { Calendar } from '@/components/ui/calendar';
import {
  Popover,
  PopoverContent,
  PopoverTrigger,
} from '@/components/ui/popover';
import { cn } from '@/lib/utils';
import { esDiaDeshabilitado } from './date-picker-utils';

/**
 * <c>&lt;DatePickerField/&gt;</c> — date picker para <c>DateOnly</c>
 * del backend (formato ISO <c>YYYY-MM-DD</c>, sin hora ni TZ).
 * Doc 05 §11.3.
 *
 * <para>Diseño:</para>
 * <list>
 *   <item>Trigger button muestra la fecha en formato <c>dd/MMM/yyyy</c>
 *   en español (ej. <c>02/may/2026</c>).</item>
 *   <item>Popover con <c>&lt;Calendar/&gt;</c> de shadcn (envuelve
 *   <c>react-day-picker</c>) en español.</item>
 *   <item>Output: ISO <c>YYYY-MM-DD</c> directo, listo para mandar al
 *   backend en el campo <c>fechaEntregaDeseada</c>, etc.</item>
 * </list>
 *
 * <para>NO maneja hora ni TZ — para timestamps con hora, usar otro
 * componente (no entra en UF2-PR1, ningún campo de Compras lo pide).</para>
 */
export interface DatePickerFieldProps {
  /** ISO <c>YYYY-MM-DD</c> o <c>null</c>/<c>undefined</c>. */
  value: string | null | undefined;
  onChange: (value: string | null) => void;
  placeholder?: string;
  disabled?: boolean;
  className?: string;
  id?: string;
  /** Día mínimo seleccionable (por defecto sin restricción). */
  minDate?: Date;
  /** Día máximo seleccionable. */
  maxDate?: Date;
}

export function DatePickerField({
  value,
  onChange,
  placeholder = 'Selecciona fecha',
  disabled,
  className,
  id,
  minDate,
  maxDate,
}: DatePickerFieldProps) {
  const [open, setOpen] = useState(false);
  const date = parseDateOnly(value);

  function handleSelect(next: Date | undefined) {
    if (next == null) {
      onChange(null);
    } else {
      // Format manual a YYYY-MM-DD para evitar TZ shift (formatear
      // con date-fns usa la TZ del runtime y un Date con hora 00:00
      // local genera el día correcto).
      const yyyy = next.getFullYear();
      const mm = String(next.getMonth() + 1).padStart(2, '0');
      const dd = String(next.getDate()).padStart(2, '0');
      onChange(`${yyyy}-${mm}-${dd}`);
    }
    setOpen(false);
  }

  return (
    <Popover open={open} onOpenChange={setOpen}>
      <PopoverTrigger asChild>
        <Button
          id={id}
          type="button"
          variant="outline"
          disabled={disabled}
          className={cn(
            'w-full justify-start font-normal',
            !date && 'text-muted-foreground',
            className,
          )}
          aria-label="Seleccionar fecha"
        >
          <CalendarIcon className="mr-2 h-4 w-4" />
          {date != null ? format(date, 'dd MMM yyyy', { locale: es }) : placeholder}
        </Button>
      </PopoverTrigger>
      <PopoverContent align="start" className="w-auto p-0">
        <Calendar
          mode="single"
          selected={date ?? undefined}
          onSelect={handleSelect}
          locale={es}
          disabled={(d) => esDiaDeshabilitado(d, minDate, maxDate)}
        />
      </PopoverContent>
    </Popover>
  );
}

/**
 * Convierte el valor del campo (ISO <c>YYYY-MM-DD</c>) a
 * <c>Date</c> local sin TZ shift. <c>parseISO('2026-05-02')</c>
 * interpreta como medianoche UTC → en TZ negativa puede caer al día
 * anterior; construimos manualmente con <c>Date(year, month, day)</c>
 * para que represente medianoche local.
 */
function parseDateOnly(value: string | null | undefined): Date | null {
  if (value == null || value === '') return null;
  if (/^\d{4}-\d{2}-\d{2}$/.test(value)) {
    const [yyyy, mm, dd] = value.split('-').map((n) => parseInt(n, 10));
    const date = new Date(yyyy, mm - 1, dd);
    return isValid(date) ? date : null;
  }
  // Fallback para ISO completo (raro pero por compat).
  const date = parseISO(value);
  return isValid(date) ? date : null;
}
