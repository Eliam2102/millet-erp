import { apiFetch } from '@/lib/auth/api-client';
import { ApiError } from '@/lib/api/error';
import { descargarArchivo } from '@/features/facturacion/lib/descargas';

/**
 * Descargas/preview de los archivos de un CFDI recibido (XML/PDF) contra
 * los endpoints autenticados de CxP (<c>GET /cfdis/{id}/xml|pdf</c>).
 * Reusa el helper genérico <c>descargarArchivo</c> de Facturación (mismo
 * problema: Bearer token → no sirve un <c>&lt;a href&gt;</c> directo).
 */

export function descargarXmlCfdi(id: string, uuidCfdi: string): Promise<void> {
  return descargarArchivo(
    `/api/v1/cuentas-por-pagar/cfdis/${id}/xml`,
    `${uuidCfdi}.xml`,
  );
}

export function descargarPdfCfdi(id: string, uuidCfdi: string): Promise<void> {
  return descargarArchivo(
    `/api/v1/cuentas-por-pagar/cfdis/${id}/pdf`,
    `${uuidCfdi}.pdf`,
  );
}

/**
 * Abre el PDF del CFDI en una pestaña nueva vía object URL. Se revoca
 * tras un minuto — suficiente para que la pestaña cargue el documento
 * (el blob ya vive en memoria de la pestaña, revocar no lo cierra).
 */
export async function abrirPdfCfdi(id: string): Promise<void> {
  const response = await apiFetch(`/api/v1/cuentas-por-pagar/cfdis/${id}/pdf`);
  if (!response.ok) {
    let problem;
    try {
      problem = await response.json();
    } catch {
      problem = {
        type: 'about:blank',
        title: response.statusText || 'Error al abrir el PDF',
        status: response.status,
        code: 'PDF_FETCH_ERROR',
      };
    }
    throw new ApiError(problem, response.status);
  }
  const blob = await response.blob();
  const objectUrl = URL.createObjectURL(blob);
  window.open(objectUrl, '_blank', 'noopener');
  setTimeout(() => URL.revokeObjectURL(objectUrl), 60_000);
}
