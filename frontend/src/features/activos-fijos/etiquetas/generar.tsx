import { pdf } from '@react-pdf/renderer';
import type { Etiqueta } from '../api/conservacion';
import { EtiquetasPdf } from './EtiquetasPdf';
export async function generarEtiquetas(etiquetas: Etiqueta[]) {
  if (etiquetas.length === 0) throw new Error('No hay activos con los filtros de impresión.');
  return pdf(<EtiquetasPdf etiquetas={etiquetas} />).toBlob();
}
