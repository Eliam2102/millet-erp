import type { ReactNode } from 'react';
import { formatMoney } from '@/lib/money';
import { cn } from '@/lib/utils';

interface Importe {
  etiqueta: string;
  monto: number;
}
export interface ResumenImportesProps {
  moneda: string;
  subtotal?: number;
  descuento?: number;
  ivaTrasladado?: number;
  otrosTrasladados?: Importe[];
  ivaRetenido?: number;
  isrRetenido?: number;
  otrasRetenciones?: Importe[];
  total: number;
  estadoPago?: (Importe & { destacado?: boolean })[];
  acciones?: ReactNode;
  className?: string;
}

/** Importes en una columna; separa el total del estado de pago y las acciones. */
export function ResumenImportes({
  moneda,
  subtotal,
  descuento,
  ivaTrasladado,
  otrosTrasladados,
  ivaRetenido,
  isrRetenido,
  otrasRetenciones,
  total,
  estadoPago,
  acciones,
  className,
}: ResumenImportesProps) {
  const filas = [
    { etiqueta: 'Subtotal', monto: subtotal },
    { etiqueta: 'Descuento', monto: descuento, resta: true },
    { etiqueta: 'IVA trasladado', monto: ivaTrasladado },
    ...(otrosTrasladados ?? []),
    { etiqueta: 'IVA retenido', monto: ivaRetenido, resta: true },
    { etiqueta: 'ISR retenido', monto: isrRetenido, resta: true },
    ...(otrasRetenciones ?? []).map((fila) => ({ ...fila, resta: true })),
  ];
  return (
    <div className={cn('ml-auto w-full max-w-[380px] space-y-4 text-sm text-ink', className)}>
      <dl aria-label="Resumen de importes" className="space-y-2">
        {filas.map(
          (fila, index) =>
            fila.monto !== undefined && (
              <div key={index} className="flex items-baseline justify-between gap-4">
                <dt className="text-ink-strong">{fila.etiqueta}</dt>
                <dd className="text-right tabular-nums">
                  {'resta' in fila && fila.resta && fila.monto !== 0 ? '−' : ''}
                  {formatMoney({
                    amount: 'resta' in fila && fila.resta ? Math.abs(fila.monto) : fila.monto,
                    currency: moneda,
                  })}
                </dd>
              </div>
            ),
        )}
        <div className="flex items-baseline justify-between gap-4 border-t border-line pt-3 text-base font-semibold">
          <dt>Total</dt>
          <dd className="text-right tabular-nums">
            {formatMoney({ amount: total, currency: moneda })}
          </dd>
        </div>
      </dl>
      {!!estadoPago?.length && (
        <dl aria-label="Estado de pago" className="space-y-2 border-t border-line pt-3">
          {estadoPago.map((fila, index) => (
            <div
              key={index}
              className={cn(
                'flex items-baseline justify-between gap-4',
                fila.destacado && 'font-semibold',
              )}
            >
              <dt>{fila.etiqueta}</dt>
              <dd className="text-right tabular-nums">
                {formatMoney({ amount: fila.monto, currency: moneda })}
              </dd>
            </div>
          ))}
        </dl>
      )}
      {acciones != null && <div className="flex flex-wrap justify-end gap-2">{acciones}</div>}
    </div>
  );
}
