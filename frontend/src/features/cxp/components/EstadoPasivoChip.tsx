import { cn } from '@/lib/utils';
import {
  EstadoPasivo,
  EstadoPasivoLabels,
} from '@/features/cxp/api/types';

/**
 * <c>&lt;EstadoPasivoChip/&gt;</c> — badge de color por estado del
 * pasivo. Mismo patrón visual que los otros chips del módulo.
 *
 * <para>Colores: Capturada azul (en captura), EnRevision ámbar (acción
 * de revisión requerida), Autorizada verde (terminal positivo), Pagada
 * teal (terminal definitivo), Cancelada gris/rosado (anomalía).</para>
 */
export function EstadoPasivoChip({ estado }: { estado: EstadoPasivo }) {
  return (
    <span
      className={cn(
        'inline-flex items-center rounded-full px-2 py-0.5 text-xs font-medium',
        estado === EstadoPasivo.Capturada && 'bg-blue-100 text-blue-800',
        estado === EstadoPasivo.EnRevision && 'bg-amber-100 text-amber-800',
        estado === EstadoPasivo.Autorizada &&
          'bg-emerald-100 text-emerald-800',
        estado === EstadoPasivo.Pagada && 'bg-teal-100 text-teal-800',
        estado === EstadoPasivo.Cancelada && 'bg-rose-100 text-rose-800',
      )}
    >
      {EstadoPasivoLabels[estado]}
    </span>
  );
}
