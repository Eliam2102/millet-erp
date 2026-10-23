import { CircleCheck, TriangleAlert } from 'lucide-react';
import { cn } from '@/lib/utils';
import { centavos, monto } from '../../lib/polizas';

/**
 * Indicador de cuadre (C1.3): total de cargos, total de abonos y diferencia. Cuadra cuando son iguales y distintos de cero;
 * el estado se comunica con texto además del color (DESIGN §8).
 */
export function CuadrePoliza({
  totalCargos,
  totalAbonos,
  nota,
}: {
  totalCargos: number;
  totalAbonos: number;
  nota?: string;
}) {
  const diferencia = centavos(totalCargos - totalAbonos);
  const cuadra = diferencia === 0 && totalCargos !== 0;
  return (
    <section
      role="status"
      aria-label="Cuadre de la póliza"
      className={cn(
        'rounded-lg px-4 py-3 shadow-card-flat',
        cuadra ? 'bg-success-bg text-success-fg' : 'bg-danger-bg text-danger-fg',
      )}
    >
      <dl className="ml-auto w-full max-w-96 space-y-2 text-sm">
        <div className="flex items-baseline justify-between gap-4">
          <dt className="text-xs text-ink-muted">Total cargos</dt>
          <dd className="text-right font-medium tabular-nums">{monto(totalCargos)}</dd>
        </div>
        <div className="flex items-baseline justify-between gap-4">
          <dt className="text-xs text-ink-muted">Total abonos</dt>
          <dd className="font-medium tabular-nums">{monto(totalAbonos)}</dd>
        </div>
        <div className="flex items-baseline justify-between gap-4">
          <dt className="text-xs text-ink-muted">Diferencia</dt>
          <dd
            className={cn(
              'font-medium tabular-nums',
              !cuadra && diferencia !== 0 && 'text-danger-fg',
            )}
          >
            {monto(diferencia)}
          </dd>
        </div>
      </dl>
      <p className="mt-2 flex items-center gap-1.5 text-xs font-medium">
        {cuadra ? (
          <>
            <CircleCheck className="size-3.5" strokeWidth={1.8} aria-hidden="true" />
            Cuadra: cargos y abonos son iguales.
          </>
        ) : (
          <>
            <TriangleAlert
              className="size-3.5 text-danger-fg"
              strokeWidth={1.8}
              aria-hidden="true"
            />
            {totalCargos === 0 && totalAbonos === 0
              ? 'No cuadra: captura los importes.'
              : 'No cuadra: cargos y abonos no son iguales.'}
          </>
        )}
      </p>
      {nota && !cuadra && <p className="mt-1 text-xs text-ink-muted">{nota}</p>}
    </section>
  );
}
