/**
 * Exportador PDF cross-módulo para <c>&lt;ReporteShell&gt;</c>
 * (ADR-0036). Lazy-loaded: este módulo se importa dinámicamente solo
 * cuando el usuario hace click en "Descargar PDF", evitando inflar el
 * bundle inicial con <c>@react-pdf/renderer</c> (~500KB gzipped).
 *
 * <para>El documento React-PDF en sí vive en
 * <c>ReporteDocument.tsx</c> (separado por la regla
 * <c>react-refresh/only-export-components</c>: este archivo solo
 * exporta funciones helper, no componentes).</para>
 */
import { pdf } from '@react-pdf/renderer';
import {
  ReporteDocument,
  type ReporteParaExportar,
} from '@/components/erp/reportes/exporters/ReporteDocument';

export type { ReporteParaExportar };

/**
 * Genera un Blob PDF y dispara la descarga en el browser.
 */
export async function descargarReportePdf<TFila>(
  reporte: ReporteParaExportar<TFila>,
  nombreArchivo: string,
): Promise<void> {
  const obtenerCelda = (row: TFila, clave: string): unknown =>
    (row as unknown as Record<string, unknown>)[clave];

  const blob = await pdf(
    <ReporteDocument reporte={reporte} obtenerCelda={obtenerCelda} />,
  ).toBlob();

  const url = URL.createObjectURL(blob);
  const a = document.createElement('a');
  a.href = url;
  a.download = nombreArchivo.endsWith('.pdf')
    ? nombreArchivo
    : `${nombreArchivo}.pdf`;
  document.body.appendChild(a);
  a.click();
  document.body.removeChild(a);
  URL.revokeObjectURL(url);
}
