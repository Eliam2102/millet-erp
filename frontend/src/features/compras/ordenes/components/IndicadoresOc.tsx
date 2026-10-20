import { RecurrenteBadge } from '@/features/compras/components/IndicadoresPendiente';
import type { OrdenPendienteAutorizacion } from '@/features/compras/ordenes/api/usePendientesAutorizacionOc';

/** El estado ocupa el primer badge de la fila; recurrencia tiene prioridad. */
export function IndicadoresOc({ oc }: { oc: OrdenPendienteAutorizacion }) {
  return (
    <div className="flex flex-wrap items-center gap-1 text-xs text-ink-muted">
      <RecurrenteBadge esRecurrente={oc.esRecurrente} />
      <span>
        {[
          oc.requiereNivel2 ? 'Falta N2' : 'Solo N1',
          oc.proveedorConocido && 'Proveedor conocido',
          oc.esMenorCuantia && 'Menor cuantía',
        ]
          .filter(Boolean)
          .join(' · ')}
      </span>
    </div>
  );
}
