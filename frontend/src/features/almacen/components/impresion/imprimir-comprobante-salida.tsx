/**
 * Helper que genera + descarga el PDF del comprobante de salida.
 * Lazy-loaded: este módulo se importa dinámicamente solo al hacer
 * click en "Imprimir comprobante", evitando inflar el bundle inicial
 * con <c>@react-pdf/renderer</c> (~500KB gzipped).
 *
 * <para>El documento React-PDF vive en
 * <c>ComprobanteSalidaDocument.tsx</c> (separado por la regla
 * <c>react-refresh/only-export-components</c>: este archivo solo
 * exporta funciones helper).</para>
 */
import { pdf } from '@react-pdf/renderer';
import {
  ComprobanteSalidaDocument,
  type ComprobanteSalidaData,
} from '@/features/almacen/components/impresion/ComprobanteSalidaDocument';

export type { ComprobanteSalidaData };

/**
 * Genera el PDF y dispara la descarga en el browser. Nombre del
 * archivo: <c>{folio}.pdf</c>.
 */
export async function imprimirComprobanteSalida(
  data: ComprobanteSalidaData,
): Promise<void> {
  const blob = await pdf(
    <ComprobanteSalidaDocument data={data} />,
  ).toBlob();

  const url = URL.createObjectURL(blob);
  const a = document.createElement('a');
  a.href = url;
  a.download = `${data.salida.folio}.pdf`;
  document.body.appendChild(a);
  a.click();
  document.body.removeChild(a);
  URL.revokeObjectURL(url);
}
