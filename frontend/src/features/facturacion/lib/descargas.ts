import { apiFetch } from '@/lib/auth/api-client';
import { ApiError } from '@/lib/api/error';

/**
 * Descarga un archivo de un endpoint autenticado (XML/PDF del CFDI,
 * B7/B8). El endpoint requiere Bearer token, así que no se puede usar un
 * <c>&lt;a href&gt;</c> directo; se hace fetch vía <c>apiFetch</c> (que
 * inyecta el token), se obtiene el Blob y se dispara la descarga con un
 * <c>&lt;a download&gt;</c> temporal. Mismo enfoque que
 * <c>usePdfOrdenCompra</c> de Compras OC.
 */
export async function descargarArchivo(
  url: string,
  nombrePorDefecto: string,
): Promise<void> {
  const response = await apiFetch(url);
  if (!response.ok) {
    let problem;
    try {
      problem = await response.json();
    } catch {
      problem = {
        type: 'about:blank',
        title: response.statusText || 'Error al descargar el archivo',
        status: response.status,
        code: 'DESCARGA_ERROR',
      };
    }
    throw new ApiError(problem, response.status);
  }

  const blob = await response.blob();
  const nombre =
    nombreDesdeContentDisposition(response.headers.get('Content-Disposition')) ??
    nombrePorDefecto;

  const objectUrl = URL.createObjectURL(blob);
  try {
    const a = document.createElement('a');
    a.href = objectUrl;
    a.download = nombre;
    document.body.appendChild(a);
    a.click();
    a.remove();
  } finally {
    URL.revokeObjectURL(objectUrl);
  }
}

/** Extrae el filename de un header Content-Disposition, si viene. */
function nombreDesdeContentDisposition(cd: string | null): string | null {
  if (!cd) return null;
  const utf8 = cd.match(/filename\*=UTF-8''([^;]+)/i);
  if (utf8?.[1]) return decodeURIComponent(utf8[1]);
  const simple = cd.match(/filename="?([^";]+)"?/i);
  return simple?.[1] ?? null;
}
