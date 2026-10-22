import { pdf } from '@react-pdf/renderer';
import type { ProformaPedidoResponse } from '@/features/facturacion/api/types';
import { ProformaCfdiDocument } from './ProformaCfdiDocument';

/** `proforma-{pedido}-v{n}.pdf`; sin número de pedido usa el folio PF. */
export function nombreArchivoProforma(proforma: ProformaPedidoResponse): string {
  const base = (proforma.numeroPedido ?? proforma.folio).replace(/[^\w.-]+/g, '_');
  return `proforma-${base}-v${proforma.version}.pdf`;
}

/** PDF de la proforma como Blob, para verlo en pantalla (visor) o descargarlo. */
export function generarProformaPdfBlob(proforma: ProformaPedidoResponse): Promise<Blob> {
  return pdf(<ProformaCfdiDocument proforma={proforma} />).toBlob();
}

/** Genera y descarga el PDF de la proforma (se importa en diferido). */
export async function descargarProformaCfdi(proforma: ProformaPedidoResponse) {
  const blob = await generarProformaPdfBlob(proforma);
  const url = URL.createObjectURL(blob);
  const a = document.createElement('a');
  a.href = url;
  a.download = nombreArchivoProforma(proforma);
  document.body.appendChild(a);
  try {
    a.click();
  } finally {
    a.remove();
    URL.revokeObjectURL(url);
  }
}
