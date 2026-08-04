import { formatMoney, type Money } from '@/lib/money';
import { cn } from '@/lib/utils';

/**
 * <c>&lt;MoneyDisplay/&gt;</c> — wrapper de <see cref="formatMoney"/>
 * con clases tipográficas tabulares (las cifras alinean por columna en
 * tablas). Doc 05 §11.3.
 *
 * <para>Acepta o el value object <c>{ amount, currency }</c> directo,
 * o los dos campos sueltos como vienen en <c>LineaResponse</c>
 * (<c>precioEstimadoMonto</c> + <c>precioEstimadoMoneda</c>).</para>
 *
 * <para>Reemplaza el dato con un guion <c>—</c> si el valor es
 * <c>null</c>/<c>undefined</c> — coherente con el resto de la UI.</para>
 */
export type MoneyDisplayProps =
  | {
      money: Money | null | undefined;
      amount?: never;
      currency?: never;
      className?: string;
      /** Override del placeholder cuando el valor es nullish. */
      empty?: string;
    }
  | {
      money?: never;
      amount: number | null | undefined;
      currency: string | null | undefined;
      className?: string;
      empty?: string;
    };

export function MoneyDisplay(props: MoneyDisplayProps) {
  const { className, empty = '—' } = props;
  const money: Money | null =
    props.money !== undefined && props.money !== null
      ? props.money
      : props.amount != null && props.currency != null
        ? { amount: props.amount, currency: props.currency }
        : null;

  if (money == null) {
    return (
      <span
        className={cn('text-muted-foreground', className)}
        aria-label="Sin monto"
      >
        {empty}
      </span>
    );
  }

  return (
    <span className={cn('tabular-nums', className)}>{formatMoney(money)}</span>
  );
}
