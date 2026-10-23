import { formatMoney } from '@/lib/money';
import { cn } from '@/lib/utils';

export interface MontoMovimientoProps {
  monto: number;
  tipo: 'entrada' | 'salida';
  moneda: string;
}
/** Distingue entradas y salidas por signo y tono semántico. */
export function MontoMovimiento({ monto, tipo, moneda }: MontoMovimientoProps) {
  return (
    <span className={cn('tabular-nums', tipo === 'entrada' ? 'text-success-fg' : 'text-danger-fg')}>
      {tipo === 'entrada' ? '+' : '−'}
      {formatMoney({ amount: Math.abs(monto), currency: moneda })}
    </span>
  );
}
