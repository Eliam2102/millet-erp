import { EstadoLineaCredito } from '@/features/cxc/api/types';
import { ETIQUETA_ESTADO_LINEA } from '@/features/cxc/lib/glosario';
import { cn } from '@/lib/utils';

/**
 * <c>&lt;ChipEstadoLinea/&gt;</c> — badge de estado de una línea de
 * crédito (patrón <c>ChipTimbrado</c> de Facturación). Activa = verde,
 * Bloqueada = rojo, Suspendida = gris.
 */
export function ChipEstadoLinea({ estado }: { estado: EstadoLineaCredito }) {
  const clases: Record<EstadoLineaCredito, string> = {
    [EstadoLineaCredito.Activa]:
      'bg-emerald-100 text-emerald-800 dark:bg-emerald-950 dark:text-emerald-300',
    [EstadoLineaCredito.Bloqueada]:
      'bg-red-100 text-red-800 dark:bg-red-950 dark:text-red-300',
    [EstadoLineaCredito.Suspendida]:
      'bg-muted text-muted-foreground',
  };
  return (
    <span
      className={cn(
        'inline-flex shrink-0 items-center rounded-full px-2 py-0.5 text-[11px] font-medium',
        clases[estado],
      )}
    >
      {ETIQUETA_ESTADO_LINEA[estado]}
    </span>
  );
}
