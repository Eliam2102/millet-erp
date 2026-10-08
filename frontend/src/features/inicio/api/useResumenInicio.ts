import { useAuthStore } from '@/lib/auth/auth-store';
import { pendientes, pendienteVisible, type PendienteConfig } from '../config';
import { calcularPrioridad, compararPrioridad, type PrioridadInicio } from '../prioridad';
import { nivelRequisiciones, useConteosInicio } from './useConteosInicio';

export interface IndicadorInicio {
  fila: PendienteConfig;
  prioridad: PrioridadInicio;
  estado: 'cargando' | 'error' | 'listo';
  refetch: () => unknown;
  isFetching: boolean;
}

export function useResumenInicio() {
  const permisos = useAuthStore((s) => s.permisos);
  const visibles = pendientes.filter((fila) => fila.conteo && pendienteVisible(fila, permisos));
  const ids = visibles.flatMap((fila) => (fila.conteo ? [fila.conteo] : []));
  const queries = useConteosInicio(ids, permisos);
  const nivel = nivelRequisiciones(permisos);
  const ahora = new Date();
  const indicadores: IndicadorInicio[] = visibles.map((fila, index) => {
    const query = queries[index];
    return {
      fila:
        fila.conteo === 'requisiciones' && nivel !== undefined
          ? { ...fila, search: { nivelPendiente: nivel } }
          : fila,
      prioridad: calcularPrioridad(
        query.isError ? { total: 0 } : (query.data ?? { total: 0 }),
        ahora,
      ),
      estado: query.isPending ? 'cargando' : query.isError ? 'error' : 'listo',
      refetch: query.refetch,
      isFetching: query.isFetching,
    };
  });
  indicadores.sort((a, b) => compararPrioridad(a.prioridad, b.prioridad));
  return {
    // Pendiente: pin/personalización cuando exista persistencia de preferencias.
    kpis: indicadores.slice(0, 4),
    filas: indicadores.filter((i) => i.estado === 'listo' && i.prioridad.total > 0),
    errores: indicadores.filter((i) => i.estado === 'error'),
    cargando: indicadores.some((i) => i.estado === 'cargando'),
  };
}
