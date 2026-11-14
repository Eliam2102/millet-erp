import { formatDate } from '@/lib/datetime';
import type { ReaperturaPeriodo } from '../api/periodos';
import type { PolizaResumen } from '../api/polizas-types';

/** H-U29: etiqueta legible de una póliza: folio, periodo y concepto (nunca el identificador). */
export function etiquetaPoliza(p: Pick<PolizaResumen, 'folio' | 'periodoAnio' | 'periodoNumero' | 'concepto'>): string {
  const periodo = `${p.periodoAnio}-${String(p.periodoNumero).padStart(2, '0')}`;
  return `${p.folio ?? 'Sin folio'} · ${periodo} · ${p.concepto}`;
}

/** H-U29: etiqueta legible de una reapertura: cuándo se abrió, hasta cuándo y su evidencia. */
export function etiquetaReapertura(r: ReaperturaPeriodo): string {
  const estado = r.vigente ? `vence ${formatDate(r.venceEn)}` : 'concluida';
  return `${formatDate(r.creadaEn)} · ${estado} · ${r.evidenciaReferencia}`;
}
