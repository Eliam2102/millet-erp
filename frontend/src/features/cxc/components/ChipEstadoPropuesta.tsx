import { EstadoPropuestaAplicacion } from '@/features/cxc/api/types';
import { ETIQUETA_ESTADO_PROPUESTA } from '@/features/cxc/lib/glosario';
import { cn } from '@/lib/utils';

/**
 * <c>&lt;ChipEstadoPropuesta/&gt;</c> — badge de estado de una propuesta
 * de aplicación. Propuesta = azul (pendiente de Ingresos), Confirmada =
 * verde, Rechazada = rojo.
 */
export function ChipEstadoPropuesta({
  estado,
}: {
  estado: EstadoPropuestaAplicacion;
}) {
  const clases: Record<EstadoPropuestaAplicacion, string> = {
    [EstadoPropuestaAplicacion.Propuesta]:
      'bg-sky-100 text-sky-800 dark:bg-sky-950 dark:text-sky-300',
    [EstadoPropuestaAplicacion.Confirmada]:
      'bg-emerald-100 text-emerald-800 dark:bg-emerald-950 dark:text-emerald-300',
    [EstadoPropuestaAplicacion.Rechazada]:
      'bg-red-100 text-red-800 dark:bg-red-950 dark:text-red-300',
  };
  return (
    <span
      className={cn(
        'inline-flex shrink-0 items-center rounded-full px-2 py-0.5 text-[11px] font-medium',
        clases[estado],
      )}
    >
      {ETIQUETA_ESTADO_PROPUESTA[estado]}
    </span>
  );
}
