import { ResultadoLiberacion, type ReevaluacionLiberacionResponse } from '@/features/cxc/api/types';
import { ETIQUETA_RESULTADO_LIBERACION } from '@/features/cxc/lib/glosario';

/** M-238: aviso del resultado de reevaluar (libera, sigue retenido o queda pendiente de autorización). */
export function avisoReevaluacion(res: ReevaluacionLiberacionResponse): {
  tipo: 'success' | 'warning';
  titulo: string;
} {
  const { nueva } = res;
  const etiqueta = ETIQUETA_RESULTADO_LIBERACION[nueva.resultado];
  if (nueva.resultado === ResultadoLiberacion.Liberado || nueva.resultado === ResultadoLiberacion.LiberadoConOverride)
    return { tipo: 'success', titulo: `Pedido ${nueva.pedidoRef} liberado tras la corrección (${etiqueta}).` };
  return {
    tipo: 'warning',
    titulo: `Pedido ${nueva.pedidoRef} reevaluado: ${etiqueta.toLowerCase()}. Revisa el historial del pedido.`,
  };
}
