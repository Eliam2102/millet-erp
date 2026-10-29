import { apiFetch } from '@/lib/auth/api-client';
import { ApiError } from '@/lib/api/error';

/**
 * Descarga el comprobante de salida con el formato M2 de Millet
 * (<c>GET /salidas/{id}/comprobante</c>, dibujado en backend). Mismo patrón
 * que el vale: el endpoint pide Bearer, así que se baja como Blob.
 */
export async function descargarComprobanteSalida(salidaId: string, folio: string): Promise<void> {
  const response = await apiFetch(`/api/v1/almacen/salidas/${salidaId}/comprobante`);
  if (!response.ok) {
    let problem;
    try {
      problem = await response.json();
    } catch {
      problem = {
        type: 'about:blank',
        title: response.statusText || 'Error al descargar el comprobante',
        status: response.status,
        code: 'COMPROBANTE_FETCH_ERROR',
      };
    }
    throw new ApiError(problem, response.status);
  }
  const url = URL.createObjectURL(await response.blob());
  const enlace = document.createElement('a');
  enlace.href = url;
  enlace.download = `${folio}.pdf`;
  document.body.appendChild(enlace);
  enlace.click();
  enlace.remove();
  setTimeout(() => URL.revokeObjectURL(url), 1000);
}
