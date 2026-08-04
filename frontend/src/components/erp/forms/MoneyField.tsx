import { type ComponentProps } from 'react';
import { Input } from '@/components/ui/input';
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from '@/components/ui/select';
import { type Money } from '@/lib/money';
import { cn } from '@/lib/utils';

/**
 * <c>&lt;MoneyField/&gt;</c> — input compuesto para capturar un
 * <c>Money</c> ({ amount, currency }). Doc 05 §11.3 + ADR-0014.
 *
 * <para>Diseño:</para>
 * <list>
 *   <item>Input numérico (decimales) para el monto.</item>
 *   <item>Select compacto a la derecha con las monedas soportadas
 *   (default MXN; USD, EUR, GBP también).</item>
 * </list>
 *
 * <para>API agnóstica de RHF: <c>value</c> + <c>onChange</c>. El
 * caller usa <c>Controller</c> de react-hook-form si trabaja con
 * formularios. Internamente, parsea el string del input a
 * <c>amount: number</c> con <c>parseFloat</c> y propaga; si el input
 * es vacío, devuelve <c>null</c> para que Zod lo refuse o el caller
 * lo trate como nullable.</para>
 *
 * <para>NO formatea con separadores de miles mientras el usuario
 * escribe (eso interfiere con el cursor); el formateo "1,234.56" lo
 * hace <c>&lt;MoneyDisplay/&gt;</c> en read-only contexts.</para>
 */
export interface MoneyFieldProps {
  /**
   * Valor controlled. <c>null</c>/<c>undefined</c> = empty input;
   * conserva la <c>currency</c> default visible para que el usuario
   * no tenga que escogerla cada vez.
   */
  value: Money | null | undefined;
  onChange: (value: Money | null) => void;
  /** Default <c>'MXN'</c>. */
  defaultCurrency?: string;
  /** Lista de monedas seleccionables. Default <c>['MXN','USD','EUR']</c>. */
  currencies?: string[];
  disabled?: boolean;
  className?: string;
  /** Atributos del input (placeholder, id, aria-*, etc.). */
  inputProps?: Omit<
    ComponentProps<typeof Input>,
    'value' | 'onChange' | 'type' | 'inputMode'
  >;
}

const DEFAULT_CURRENCIES = ['MXN', 'USD', 'EUR'];

export function MoneyField({
  value,
  onChange,
  defaultCurrency = 'MXN',
  currencies = DEFAULT_CURRENCIES,
  disabled,
  className,
  inputProps,
}: MoneyFieldProps) {
  const currentAmount = value?.amount;
  const currentCurrency = value?.currency ?? defaultCurrency;

  function handleAmountChange(input: string) {
    if (input === '' || input === '-') {
      onChange(null);
      return;
    }
    const parsed = Number.parseFloat(input);
    if (!Number.isFinite(parsed)) return; // input inválido → ignorar
    onChange({ amount: parsed, currency: currentCurrency });
  }

  function handleCurrencyChange(next: string) {
    if (currentAmount == null) {
      // Sin monto, igual recordamos la moneda seleccionada para próximo
      // input — set un Money "vacío" no es ideal; lo manejamos como
      // null + dejamos el select reflejando la nueva default.
      onChange(null);
      return;
    }
    onChange({ amount: currentAmount, currency: next });
  }

  return (
    <div className={cn('flex gap-2', className)}>
      <Input
        type="number"
        inputMode="decimal"
        step="0.01"
        value={currentAmount ?? ''}
        onChange={(e) => handleAmountChange(e.target.value)}
        disabled={disabled}
        className="flex-1 tabular-nums"
        {...inputProps}
      />
      <Select
        value={currentCurrency}
        onValueChange={handleCurrencyChange}
        disabled={disabled}
      >
        <SelectTrigger className="w-24" aria-label="Moneda">
          <SelectValue />
        </SelectTrigger>
        <SelectContent>
          {currencies.map((c) => (
            <SelectItem key={c} value={c}>
              {c}
            </SelectItem>
          ))}
        </SelectContent>
      </Select>
    </div>
  );
}
