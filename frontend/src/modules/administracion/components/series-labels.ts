import {
  ReinicioPeriodo,
  TipoDocumentoSerie,
} from '@/modules/administracion/api/types';

/**
 * Etiquetas display de los enums de Series. Notar la asimetría
 * deliberada del spec UF-Admin-PR6: el backend usa <c>None</c> pero
 * la UI muestra <b>"Eterno"</b> (decisión del owner — no hay
 * reinicio = secuencia eterna, más legible que "None").
 */

export const TIPO_DOCUMENTO_LABEL: Record<TipoDocumentoSerie, string> = {
  [TipoDocumentoSerie.OrdenCompra]: 'Orden de compra',
  [TipoDocumentoSerie.Cfdi]: 'CFDI',
  [TipoDocumentoSerie.NotaCredito]: 'Nota de crédito',
  [TipoDocumentoSerie.Poliza]: 'Póliza',
  [TipoDocumentoSerie.FacturaAnticipo]: 'Factura de anticipo',
};

export const REINICIO_PERIODO_LABEL: Record<ReinicioPeriodo, string> = {
  [ReinicioPeriodo.None]: 'Eterno',
  [ReinicioPeriodo.Anual]: 'Anual',
  [ReinicioPeriodo.Mensual]: 'Mensual',
};

export const REINICIO_PERIODO_DESCRIPCION: Record<ReinicioPeriodo, string> = {
  [ReinicioPeriodo.None]:
    'Sin reinicio. La secuencia crece siempre, sin segmento de período.',
  [ReinicioPeriodo.Anual]:
    'Reinicia el 1° de enero. Folios incluyen el año (YYYY).',
  [ReinicioPeriodo.Mensual]:
    'Reinicia cada mes. Folios incluyen año y mes (YYYY-MM).',
};

/**
 * Aproximación client-side del próximo folio para previews del
 * formulario de alta (el backend no calcula preview sin tener id).
 * El formato real lo confirma el backend en <c>SerieDetalleResponse</c>.
 */
export function previewFolioAproximado(
  prefijo: string,
  sufijo: string | null,
  reinicioPeriodo: ReinicioPeriodo,
): string {
  const num = '000001';
  if (reinicioPeriodo === ReinicioPeriodo.None) {
    return `${prefijo}${sufijo ?? ''}-${num}`;
  }
  const ahora = new Date();
  const yyyy = String(ahora.getFullYear());
  if (reinicioPeriodo === ReinicioPeriodo.Anual) {
    return `${prefijo}-${yyyy}-${num}`;
  }
  const mm = String(ahora.getMonth() + 1).padStart(2, '0');
  return `${prefijo}-${yyyy}-${mm}-${num}`;
}
