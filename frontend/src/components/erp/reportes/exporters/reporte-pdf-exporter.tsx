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
 * Genera el Blob PDF del reporte sin descargarlo (C7.8: el estado de
 * cuenta se descarga y además se adjunta al envío al cliente).
 */
export async function generarReportePdfBlob<TFila>(
  reporte: ReporteParaExportar<TFila>,
  obtenerCelda: (row: TFila, clave: string) => unknown = (row, clave) =>
    (row as Record<string, unknown>)[clave],
): Promise<Blob> {
  return pdf(<ReporteDocument reporte={reporte} obtenerCelda={obtenerCelda} />).toBlob();
}

/** Dispara la descarga de un Blob PDF en el browser. */
export function descargarBlob(blob: Blob, nombreArchivo: string): void {
  const url = URL.createObjectURL(blob);
  const a = document.createElement('a');
  a.href = url;
  a.download = nombreArchivoPdf(nombreArchivo);
  document.body.appendChild(a);
  a.click();
  document.body.removeChild(a);
  setTimeout(() => URL.revokeObjectURL(url), 1000);
}

/** Agrega la extensión <c>.pdf</c> si falta. */
export function nombreArchivoPdf(nombreArchivo: string): string {
  return /\.pdf$/i.test(nombreArchivo) ? nombreArchivo : `${nombreArchivo}.pdf`;
}

/**
 * Genera un Blob PDF y dispara la descarga en el browser.
 */
export async function descargarReportePdf<TFila>(
  reporte: ReporteParaExportar<TFila>,
  nombreArchivo: string,
  obtenerCelda?: (row: TFila, clave: string) => unknown,
): Promise<void> {
  descargarBlob(await generarReportePdfBlob(reporte, obtenerCelda), nombreArchivo);
}
