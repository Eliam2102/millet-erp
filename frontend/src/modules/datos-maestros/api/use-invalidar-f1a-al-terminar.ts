import { useEffect, useRef } from 'react';
import { useQueryClient } from '@tanstack/react-query';
import { F1A_QUERY_KEY } from '@/modules/datos-maestros/api/f1a';
import type { EstadoEjecucionSync } from '@/modules/datos-maestros/api/types';

/**
 * Invalida las consultas F1A (panel de ligados) una sola vez por ejecución,
 * en el momento en que una ejecución observada viva (Pendiente/EnCurso)
 * llega a un estado terminal. Una ejecución que ya llega terminal no
 * dispara nada: no hubo barrido que refrescar en esta vista.
 */
export function useInvalidarF1AAlTerminar(
  ejecuciones: readonly { id: string; estado: EstadoEjecucionSync }[],
) {
  const queryClient = useQueryClient();
  const vivas = useRef(new Set<string>());
  const invalidadas = useRef(new Set<string>());
  useEffect(() => {
    for (const e of ejecuciones) {
      if (e.estado === 'Pendiente' || e.estado === 'EnCurso') {
        vivas.current.add(e.id);
      } else if (vivas.current.has(e.id) && !invalidadas.current.has(e.id)) {
        invalidadas.current.add(e.id);
        queryClient.invalidateQueries({ queryKey: F1A_QUERY_KEY });
      }
    }
  }, [ejecuciones, queryClient]);
}
