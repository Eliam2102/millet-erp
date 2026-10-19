import { cn } from '@/lib/utils';
import {
  EstadoCfdiRecibido,
  EstadoCfdiRecibidoLabels,
} from '@/features/cxp/api/types';

/**
 * <c>&lt;EstadoCfdiBadge/&gt;</c> — badge de color por estado del
 * CfdiRecibido. Mismo patrón visual que <c>EstadoMovimientoBadge</c> de
 * Almacén (rounded-full + px-2 + colores por estado).
 *
 * <para>Colores: PorProcesar ámbar (acción requerida), Convertido
 * verde (terminal positivo), Duplicado púrpura (anomalía), Descartado
 * gris (terminal negativo).</para>
 */
export function EstadoCfdiBadge({ estado }: { estado: EstadoCfdiRecibido }) {
  return (
    <span
      className={cn(
        'inline-flex items-center rounded-full px-2 py-0.5 text-xs font-medium',
        estado === EstadoCfdiRecibido.PorProcesar &&
          'bg-warning-bg text-warning-fg',
        estado === EstadoCfdiRecibido.ConvertidoEnPasivo &&
          'bg-success-bg text-success-fg',
        estado === EstadoCfdiRecibido.Duplicado &&
          'bg-info-bg text-info-fg',
        estado === EstadoCfdiRecibido.Descartado && 'bg-neutral-bg text-neutral-fg',
      )}
    >
      {EstadoCfdiRecibidoLabels[estado]}
    </span>
  );
}
