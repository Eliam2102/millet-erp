import { Badge } from '@/components/ui/badge';
import { RecurrenteBadge } from '@/features/compras/components/IndicadoresPendiente';
import type { OrdenPendienteAutorizacion } from '@/features/compras/ordenes/api/usePendientesAutorizacionOc';

/**
 * Indicadores de decisión de una OC pendiente de firma. Ayudan a aprobar
 * rápido lo normal, conocido o pequeño. Colores solo por variante semántica
 * del design system (sin hex ni paleta default); el texto siempre es visible.
 */
export function IndicadoresOc({ oc }: { oc: OrdenPendienteAutorizacion }) {
  return (
    <div className="flex flex-wrap items-center gap-1">
      {oc.proveedorConocido && (
        <Badge variant="success" className="font-medium">
          Proveedor conocido
        </Badge>
      )}
      <RecurrenteBadge esRecurrente={oc.esRecurrente} />
      {oc.esMenorCuantia && (
        <Badge variant="neutral" className="font-medium">
          Menor cuantía
        </Badge>
      )}
      <Badge variant={oc.requiereNivel2 ? 'warning' : 'neutral'} className="font-medium">
        {oc.requiereNivel2 ? 'Falta N2' : 'Solo N1'}
      </Badge>
    </div>
  );
}
