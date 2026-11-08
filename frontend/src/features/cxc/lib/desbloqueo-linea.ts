import { EstadoLineaCredito, type LineaCredito } from '@/features/cxc/api/types';

/**
 * Último desbloqueo manual autorizado de la línea (C7.5: manual con autorización y motivo), para mostrarlo en el detalle.
 * Solo aplica si la línea ya no está bloqueada: mientras lo esté, el aviso es el del bloqueo. Sin desbloqueo manual, null.
 */
export function desbloqueoManual(
  linea: Pick<LineaCredito, 'estado' | 'desbloqueoManualEn' | 'motivoDesbloqueo'>,
): { en: string; motivo: string | null } | null {
  if (linea.estado === EstadoLineaCredito.Bloqueada || !linea.desbloqueoManualEn) return null;
  const motivo = linea.motivoDesbloqueo?.trim();
  return { en: linea.desbloqueoManualEn, motivo: motivo ? motivo : null };
}
