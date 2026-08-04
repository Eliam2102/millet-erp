import {
  EstadoAutorizacionCredito,
  ResultadoLiberacion,
} from '@/features/cxc/api/types';
import {
  ETIQUETA_ESTADO_AUTORIZACION,
  ETIQUETA_RESULTADO_LIBERACION,
} from '@/features/cxc/lib/glosario';
import { cn } from '@/lib/utils';

/**
 * Chips de estado del flujo de liberación (patrón <c>ChipEstadoLinea</c>).
 * Liberado = verde, Retenido = rojo, LiberadoConOverride = ámbar (pasó,
 * pero por autorización — debe saltar a la vista en auditoría).
 */
export function ChipResultadoLiberacion({
  resultado,
}: {
  resultado: ResultadoLiberacion;
}) {
  const clases: Record<ResultadoLiberacion, string> = {
    [ResultadoLiberacion.Liberado]:
      'bg-emerald-100 text-emerald-800 dark:bg-emerald-950 dark:text-emerald-300',
    [ResultadoLiberacion.Retenido]:
      'bg-red-100 text-red-800 dark:bg-red-950 dark:text-red-300',
    [ResultadoLiberacion.LiberadoConOverride]:
      'bg-amber-100 text-amber-800 dark:bg-amber-950 dark:text-amber-300',
  };
  return (
    <span
      className={cn(
        'inline-flex shrink-0 items-center rounded-full px-2 py-0.5 text-[11px] font-medium',
        clases[resultado],
      )}
    >
      {ETIQUETA_RESULTADO_LIBERACION[resultado]}
    </span>
  );
}

/** Vigente = verde, Usada = gris, Cancelada = rojo tenue. */
export function ChipEstadoAutorizacion({
  estado,
}: {
  estado: EstadoAutorizacionCredito;
}) {
  const clases: Record<EstadoAutorizacionCredito, string> = {
    [EstadoAutorizacionCredito.Autorizada]:
      'bg-emerald-100 text-emerald-800 dark:bg-emerald-950 dark:text-emerald-300',
    [EstadoAutorizacionCredito.Usada]: 'bg-muted text-muted-foreground',
    [EstadoAutorizacionCredito.Cancelada]:
      'bg-red-100 text-red-800 dark:bg-red-950 dark:text-red-300',
  };
  return (
    <span
      className={cn(
        'inline-flex shrink-0 items-center rounded-full px-2 py-0.5 text-[11px] font-medium',
        clases[estado],
      )}
    >
      {ETIQUETA_ESTADO_AUTORIZACION[estado]}
    </span>
  );
}
